namespace EZapi;

/// <summary>运行时与整包 CLI 共用的 PNG 预检。不解码图片；真正解码仍交 Unity。</summary>
internal static class PngAssets
{
    internal static string? Prepare(ItemDefinition definition, string? folder)
    {
        if (definition.Sprite is null) return null;
        if (folder is null) throw new InvalidDataException("EZAPI_ASSET: use ItemRegistry.RegisterPack for PNG assets.");
        var path = Path.GetFullPath(Path.Combine(folder, "Textures", "Items", definition.Sprite + ".png"));
        if (!File.Exists(path)) throw new InvalidDataException("EZAPI_ASSET: PNG missing: " + path);
        using var input = File.OpenRead(path);
        if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("EZAPI_ASSET: PNG exceeds 4 MiB: " + path);
        Span<byte> header = stackalloc byte[24];
        var count = 0;
        while (count < header.Length) {
            var read = input.Read(header[count..]);
            if (read == 0) break;
            count += read;
        }
        if (count != 24) throw new InvalidDataException("EZAPI_ASSET: truncated PNG signature/IHDR: " + path);
        _ = PngAssetHeader.Read(header, path);
        return path;
    }
}
