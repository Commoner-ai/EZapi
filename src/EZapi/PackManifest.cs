namespace EZapi;

/// <summary>旧三字段清单继续有效。Version 是内容包版本；MinEzapiVersion 是框架最低实现版本。</summary>
public sealed class PackManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string EzapiVersion { get; init; } = "";
    public string Version { get; init; } = "0.0.0";
    public string? MinEzapiVersion { get; init; }
    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PackDependency> Dependencies { get; init; } = Array.Empty<PackDependency>();
}
public sealed class PackDependency
{
    public string Id { get; init; } = "";
    public string MinVersion { get; init; } = "0.0.0";
}
/// <summary>仅离线声明和资源检查；不代表原生目录/解码/运行/存档已成功。</summary>
public sealed record PackValidationResult(string Folder, string? Id, string? Version, bool Valid,
    string? Error, int Items, int LootFiles, IReadOnlyList<string> Dependencies)
{
    /// <summary>CSV 资源索引；失败结果为 null。缺翻译仅警告，格式/重复键/参数不一致拒绝整包。</summary>
    public LocalizationIndex? Localization { get; init; }
}
public sealed record PackDirectoryValidationResult(bool Valid, IReadOnlyList<PackValidationResult> Packs,
    IReadOnlyList<string> LoadOrder);
