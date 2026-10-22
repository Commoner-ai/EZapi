using System.Reflection;

namespace EZapi;

/// <summary>Unity texture filtering used by an embedded sprite atlas.</summary>
public enum SpriteFilter
{
    Point = 0,
    Bilinear = 1
}

/// <summary>
/// One PNG resource embedded in a mod assembly. Width and Height describe the item's
/// grid footprint used when the sprite is assigned; they do not crop or infer a shape.
/// </summary>
public sealed record SpriteDefinition(string Key, string ResourceName, int Width, int Height)
{
    /// <summary>Horizontal pivot in normalized texture coordinates, from 0 to 1.</summary>
    public float PivotX { get; init; } = 0.5f;

    /// <summary>Vertical pivot in normalized texture coordinates, from 0 to 1.</summary>
    public float PivotY { get; init; } = 0.5f;

    /// <summary>Keep CPU-readable texture data after installation when another API needs it.</summary>
    public bool KeepReadable { get; init; }
}

internal sealed record PreparedEmbeddedSprite(SpriteDefinition Definition, byte[] Png, int PixelWidth, int PixelHeight);

/// <summary>Reads and validates assembly resources before the native atlas can be changed.</summary>
internal static class EmbeddedSpriteResources
{
    internal const int MaxSpriteCount = 128;
    internal const int MaxResourceBytes = 4 * 1024 * 1024;
    internal const int MaxAtlasBytes = 16 * 1024 * 1024;

    internal static IReadOnlyList<PreparedEmbeddedSprite> Read(
        Assembly assembly,
        IReadOnlyList<SpriteDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(definitions);
        if (assembly.IsDynamic) throw new ArgumentException("Dynamic assemblies cannot provide embedded resources.", nameof(assembly));
        if (definitions.Count < 1 || definitions.Count > MaxSpriteCount)
            throw new ArgumentOutOfRangeException(nameof(definitions), $"An atlas must contain 1..{MaxSpriteCount} sprites.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var resources = new List<PreparedEmbeddedSprite>(definitions.Count);
        var totalBytes = 0;
        foreach (var source in definitions)
        {
            if (source is null) throw new ArgumentException("Sprite definitions cannot contain null entries.", nameof(definitions));
            var definition = source with { };
            if (!IsKey(definition.Key)) throw new ArgumentException("Sprite keys must use 1..128 letters, digits, '.', '_' or '-'.", nameof(definitions));
            if (!keys.Add(definition.Key)) throw new ArgumentException("Duplicate sprite key: " + definition.Key, nameof(definitions));
            if (string.IsNullOrWhiteSpace(definition.ResourceName) || definition.ResourceName.Length > 256 ||
                definition.ResourceName.Any(char.IsControl))
                throw new ArgumentException("Embedded resource names must be non-empty and at most 256 characters.", nameof(definitions));
            if (definition.Width < 1 || definition.Width > 64 || definition.Height < 1 || definition.Height > 64)
                throw new ArgumentOutOfRangeException(nameof(definitions), "Sprite grid width and height must be in 1..64.");
            if (!float.IsFinite(definition.PivotX) || definition.PivotX < 0 || definition.PivotX > 1 ||
                !float.IsFinite(definition.PivotY) || definition.PivotY < 0 || definition.PivotY > 1)
                throw new ArgumentOutOfRangeException(nameof(definitions), "Sprite pivots must be finite values from 0 through 1.");

            Stream? stream;
            try { stream = assembly.GetManifestResourceStream(definition.ResourceName); }
            catch (Exception error) { throw new InvalidDataException("Unable to open embedded resource " + definition.ResourceName + ".", error); }
            if (stream is null) throw new InvalidDataException("Embedded sprite resource was not found: " + definition.ResourceName);
            using (stream)
            {
                if (stream.CanSeek && (stream.Length < 24 || stream.Length > MaxResourceBytes))
                    throw new InvalidDataException($"Embedded PNG must be 24 bytes..{MaxResourceBytes} bytes: {definition.ResourceName}");
                var png = ReadBounded(stream, definition.ResourceName);
                totalBytes = checked(totalBytes + png.Length);
                if (totalBytes > MaxAtlasBytes) throw new InvalidDataException($"An embedded atlas may use at most {MaxAtlasBytes} PNG bytes.");
                var dimensions = PngAssetHeader.Read(png, definition.ResourceName);
                resources.Add(new PreparedEmbeddedSprite(definition, png, dimensions.Width, dimensions.Height));
            }
        }
        return Array.AsReadOnly(resources.ToArray());
    }

    internal static bool IsKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAsciiAlphaNumeric(value[0])) return false;
        foreach (var c in value)
            if (!IsAsciiAlphaNumeric(c) && c != '.' && c != '_' && c != '-') return false;
        return true;
    }

    internal static bool IsOwnerId(string? value)
        => IsKey(value) && value!.All(c => IsAsciiAlphaNumeric(c) || c is '.' or '_' or '-');

    private static bool IsAsciiAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static byte[] ReadBounded(Stream stream, string resourceName)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0) break;
            if (buffer.Length + read > MaxResourceBytes)
                throw new InvalidDataException($"Embedded PNG exceeds {MaxResourceBytes} bytes: {resourceName}");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

