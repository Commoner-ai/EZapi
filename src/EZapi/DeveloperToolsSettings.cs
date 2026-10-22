using System.Text.Json;

namespace EZapi;

/// <summary>
/// Optional process-scoped developer tools. Missing or invalid configuration
/// disables all developer tools. This file never changes game/run/save settings.
/// </summary>
internal sealed record DeveloperToolsOptions(bool Enabled = false, bool RegressionFixtures = false);

internal static class DeveloperToolsSettings
{
    internal static DeveloperToolsOptions Current { get; private set; } = new();

    internal static void Load(string path, Action<string> warning)
    {
        Current = new();
        try
        {
            if (!File.Exists(path)) return;
            // Bound the read itself, including files that grow after the length check.
            using var file = File.OpenRead(path);
            if (file.Length > 4096) throw new InvalidDataException("Configuration exceeds 4096 bytes.");
            var bytes = new byte[4097];
            var count = file.Read(bytes, 0, bytes.Length);
            while (count < bytes.Length)
            {
                var next = file.Read(bytes, count, bytes.Length - count);
                if (next == 0) break;
                count += next;
            }
            if (count > 4096) throw new InvalidDataException("Configuration exceeds 4096 bytes.");
            Current = Parse(bytes[..count]);
        }
        catch (Exception error)
        {
            warning("EZAPI_DEVELOPER_OPTIONS: developer tools disabled (" + error.GetType().Name + "). Check UserData/EZapi/developer-tools.json.");
        }
    }

    // Keep policy pure and testable without loading Unity or touching a save.
    internal static DeveloperToolsOptions Parse(byte[] bytes)
    {
        if (bytes.Length > 4096) throw new InvalidDataException("Configuration exceeds 4096 bytes.");
        ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) json = json[3..];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object.");
        bool enabled = false, regression = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate option.");
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Expected boolean.");
            switch (property.Name)
            {
                case "enabled": enabled = property.Value.GetBoolean(); break;
                case "regressionFixtures": regression = property.Value.GetBoolean(); break;
                default: throw new InvalidDataException("Unknown option.");
            }
        }
        return new(enabled, enabled && regression);
    }

    internal static void Reset() => Current = new();
}
