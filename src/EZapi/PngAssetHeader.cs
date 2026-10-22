namespace EZapi;

internal readonly record struct PngAssetDimensions(int Width, int Height);

/// <summary>Strict PNG signature/IHDR check shared by JSON and DLL sprite loading.</summary>
internal static class PngAssetHeader
{
    internal const int MaxBytes = 4 * 1024 * 1024;

    internal static PngAssetDimensions Read(ReadOnlySpan<byte> bytes, string source)
    {
        if (bytes.Length < 24 || bytes.Length > MaxBytes ||
            !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes[12..16].SequenceEqual(new byte[] { 73, 72, 68, 82 }) ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[8..12]) != 13)
            throw new InvalidDataException("Invalid PNG signature or IHDR: " + source);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        if (width < 1 || height < 1 || width > 2048 || height > 2048)
            throw new InvalidDataException("PNG dimensions must be 1..2048: " + source);
        return new PngAssetDimensions(width, height);
    }
}
