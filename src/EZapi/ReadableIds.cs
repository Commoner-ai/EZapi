namespace EZapi;

/// <summary>原生进度 ID 的共享形状规则；不检查目录中是否存在，不改变大小写或内容。</summary>
internal static class ReadableIds
{
    internal static bool IsValid(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=160&&!id.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c));
}
