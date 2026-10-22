using MelonLoader;

namespace EZapi;

/// <summary>单个Mod的输出阈值。Off只用于配置，不能作为消息级别。</summary>
public enum LogLevel { Debug, Info, Warn, Error, Off }

/// <summary>纯托管日志规则，不读写全局配置、不修改其他Mod的日志。</summary>
public static class LogLevels
{
    /// <summary>接受英文级别（忽略大小写/首尾空格）和调试、信息、警告、错误、关闭；失败返回false，默认值为Info。</summary>
    public static bool TryParse(string? value,out LogLevel level)
    {
        level=LogLevel.Info;
        switch(value?.Trim().ToLowerInvariant()) {
            case "debug":case "调试":level=LogLevel.Debug;return true;
            case "info":case "信息":level=LogLevel.Info;return true;
            case "warn":case "warning":case "警告":level=LogLevel.Warn;return true;
            case "error":case "错误":level=LogLevel.Error;return true;
            case "off":case "关闭":level=LogLevel.Off;return true;
            default:return false;
        }
    }
    /// <summary>阈值以上消息可见；非法枚举明确抛错，Off永不成为一条消息。</summary>
    public static bool Allows(LogLevel configured,LogLevel message)
    {
        Require(configured);Require(message);
        return configured!=LogLevel.Off&&message!=LogLevel.Off&&message>=configured;
    }
    internal static void Require(LogLevel level)
    {if(level is <LogLevel.Debug or >LogLevel.Off)throw new ArgumentOutOfRangeException(nameof(level));}
}

/// <summary>不可变托管日志记录，可交给自定义文件/测试接收器；不包含原生对象或凭据。</summary>
public sealed record LogEntry(string OwnerId,LogLevel Level,string Message,bool IsCallback)
{
    public string Formatted=>"["+OwnerId+"] "+(IsCallback?"[callback] ":"")+Message;
}

/// <summary>
/// 每Mod一个实例即可：new ModLog("author.mod")。默认输出到MelonLoader，也可注入Action接收器。
/// 级别由实例独立保存；Callback是Debug级事件跟踪，不吞异常、不修改玩法流程。
/// 惰性Write只在消息可见时求值。接收器在调用线程同步执行；异常传给调用者，不能充当回调异常隔离器。
/// </summary>
public sealed class ModLog
{
    private readonly Action<LogEntry> _sink;
    public ModLog(string ownerId,LogLevel level=LogLevel.Info,Action<LogEntry>? sink=null)
    {
        if(ownerId is null||ownerId.Length>160||!System.Text.RegularExpressions.Regex.IsMatch(ownerId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))
            throw new ArgumentException("EZAPI_LOG_OWNER: lowercase namespaced owner required.",nameof(ownerId));
        LogLevels.Require(level);OwnerId=ownerId;Level=level;_sink=sink??NativeSink;
    }
    public string OwnerId {get;}
    public LogLevel Level {get;}
    public bool IsDebugEnabled=>LogLevels.Allows(Level,LogLevel.Debug);
    public void Debug(string message)=>Write(LogLevel.Debug,message);
    public void Info(string message)=>Write(LogLevel.Info,message);
    public void Warn(string message)=>Write(LogLevel.Warn,message);
    public void Error(string message)=>Write(LogLevel.Error,message);
    public void Callback(string message)=>Emit(LogLevel.Debug,message,true);
    public void Write(LogLevel level,string message)=>Emit(level,message,false);
    public void Write(LogLevel level,Func<string> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if(LogLevels.Allows(Level,level))Emit(level,message(),false);
    }
    private void Emit(LogLevel level,string message,bool callback)
    {
        ArgumentNullException.ThrowIfNull(message);
        if(LogLevels.Allows(Level,level))_sink(new(OwnerId,level,message,callback));
    }
    private static void NativeSink(LogEntry entry)
    {
        if(entry.Level==LogLevel.Error)MelonLogger.Error(entry.Formatted);
        else if(entry.Level==LogLevel.Warn)MelonLogger.Warning(entry.Formatted);
        else MelonLogger.Msg(entry.Formatted);
    }
}
