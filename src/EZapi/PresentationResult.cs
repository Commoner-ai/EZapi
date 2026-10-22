namespace EZapi;

/// <summary>展示结果。状态仅描述本次 UI/排程观察，不代表已保存或回调业务成功。</summary>
public enum PresentationStatus { Unavailable, Invalid, Conflict, Indeterminate, Queued, Arrived, Dismissed, Opened, Closed }
public sealed record PresentationResult(PresentationStatus Status,string? Reason=null);

internal static class PresentationRules
{
    internal static bool Owner(string? value)=>value is {Length: >0 and <=160}&&
        System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z");
    internal static bool Owned(string? owner,string? id)=>Owner(owner)&&Owner(id)&&id!.StartsWith(owner+".",StringComparison.Ordinal);
    internal static bool Text(string? value,int limit)=>value is not null&&value.Length<=limit&&value.IndexOf('\0')<0;
}
