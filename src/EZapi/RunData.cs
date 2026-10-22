using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>
/// 周目数据入口。本机构建缺少 ModHelper 存取便捷方法，因此由此单点适配公开 PlayerStore.modData。
/// 不调用 SaveGame，不另建存档；Set 只写当前周目的内存，持久化由游戏保存流程负责。
/// </summary>
public static class RunData
{
    public static bool IsAvailable=>GameSession.TryResolve(null,out _);
    public static RunDataStore Open(string modId)
    {
        GameSession.RequireThread();ValidateModId(modId);
        if(!TryOpen(modId,out var store))throw new InvalidOperationException("EZAPI_RUN_NOT_READY: wait for a game-loaded/gameplay event.");
        return store!;
    }
    public static bool TryOpen(string modId,out RunDataStore? store)
    {
        ValidateModId(modId);store=null;
        if(!GameSession.TryResolve(null,out _))return false;
        store=new(modId,GameSession.Identity!);return true;
    }
    internal static void ValidateModId(string modId)
    {
        if(modId is null||modId.Length>160||!Regex.IsMatch(modId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))
            throw new ArgumentException("EZAPI_RUN_MOD_ID: use a lowercase mod ID without path separators.");
    }
}

/// <summary>
/// 绑定到一次载入的托管句柄。读档、换槽、场景切换后要重新 Open；旧句柄操作抛 EZAPI_RUN_STALE。
/// 所有存取在初始化主线程执行；只清理本命名空间，不清空整个 PlayerStore.modData。
/// </summary>
public sealed class RunDataStore
{
    private const int MaxValueBytes=16384,MaxOwnerBytes=65536,MaxKeys=128;
    private static readonly JsonSerializerOptions JsonOptions=new(){MaxDepth=16};
    private readonly string _prefix;
    internal RunDataStore(string modId,GameRunIdentity identity)
        =>(ModId,Identity,_prefix)=(modId,identity,"EZapi.RunData/v1/"+modId+"/");
    public string ModId { get; }
    public GameRunIdentity Identity { get; }
    public bool IsCurrent=>GameSession.TryResolve(Identity.Epoch,out _);
    private static void ValidateKey(string key)
    {
        if(key is null||key.Length>120||!Regex.IsMatch(key,@"^[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("EZAPI_RUN_KEY: key must be 1..120 ASCII letters/digits/underscore/dot/hyphen.");
    }
    private Il2CppSystem.Collections.Generic.Dictionary<string,string>? Dictionary()
        =>GameSession.Resolve(Identity).modData;
    public bool TryGetString(string key,out string value)
    {
        ValidateKey(key);value="";
        var data=Dictionary();
        if(data is null||!data.ContainsKey(_prefix+key))return false;
        value=data[_prefix+key];return true;
    }
    public string GetString(string key,string fallback="")=>TryGetString(key,out var value)?value:fallback;
    public bool Contains(string key)=>TryGetString(key,out _);
    public IReadOnlyList<string> GetKeys()
    {
        var data=Dictionary();
        if(data is null)return Array.Empty<string>();
        var keys=new List<string>();
        foreach(var entry in data)if(entry.Key.StartsWith(_prefix,StringComparison.Ordinal))keys.Add(entry.Key[_prefix.Length..]);
        keys.Sort(StringComparer.Ordinal);return keys.AsReadOnly();
    }
    public void SetString(string key,string value)
    {
        ValidateKey(key);ArgumentNullException.ThrowIfNull(value);
        if(Encoding.UTF8.GetByteCount(value)>MaxValueBytes)throw new ArgumentException("EZAPI_RUN_LIMIT: value max 16 KiB UTF-8.");
        var player=GameSession.Resolve(Identity);
        var data=player.modData;
        var fullKey=_prefix+key;
        int count=1;long bytes=Encoding.UTF8.GetByteCount(key)+Encoding.UTF8.GetByteCount(value);
        if(data is not null)foreach(var entry in data) {
            if(entry.Key==fullKey||!entry.Key.StartsWith(_prefix,StringComparison.Ordinal))continue;
            count++;bytes+=Encoding.UTF8.GetByteCount(entry.Key[_prefix.Length..])+Encoding.UTF8.GetByteCount(entry.Value??"");
        }
        if(count>MaxKeys||bytes>MaxOwnerBytes)throw new InvalidOperationException("EZAPI_RUN_LIMIT: per mod max 128 keys / 64 KiB UTF-8 keys+values.");
        // 所有校验先完成；失败时保留旧值，不创建空字典覆盖其它 Mod。
        if(data is null) { data=new();player.modData=data; }
        data[fullKey]=value;
    }
    public bool Remove(string key)
    {
        ValidateKey(key);var data=Dictionary();return data is not null&&data.Remove(_prefix+key);
    }
    public int Clear()
    {
        var keys=GetKeys();var data=Dictionary();
        foreach(var key in keys)data!.Remove(_prefix+key);
        return keys.Count;
    }
    public int GetInt(string key,int fallback=0)=>int.TryParse(GetString(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:fallback;
    public void SetInt(string key,int value)=>SetString(key,value.ToString(CultureInfo.InvariantCulture));
    public long GetLong(string key,long fallback=0)=>long.TryParse(GetString(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:fallback;
    public void SetLong(string key,long value)=>SetString(key,value.ToString(CultureInfo.InvariantCulture));
    public bool GetBool(string key,bool fallback=false)=>bool.TryParse(GetString(key),out var value)?value:fallback;
    public void SetBool(string key,bool value)=>SetString(key,value?"true":"false");
    public float GetFloat(string key,float fallback=0)=>float.TryParse(GetString(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&float.IsFinite(value)?value:fallback;
    public void SetFloat(string key,float value)
    {
        if(!float.IsFinite(value))throw new ArgumentException("EZAPI_RUN_NUMBER: finite number required.");
        SetString(key,value.ToString("R",CultureInfo.InvariantCulture));
    }
    public double GetDouble(string key,double fallback=0)=>double.TryParse(GetString(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:fallback;
    public void SetDouble(string key,double value)
    {
        if(!double.IsFinite(value))throw new ArgumentException("EZAPI_RUN_NUMBER: finite number required.");
        SetString(key,value.ToString("R",CultureInfo.InvariantCulture));
    }
    public void SetJson<T>(string key,T value,int version=1)
    {
        if(version<1)throw new ArgumentOutOfRangeException(nameof(version));
        if(value is null)throw new ArgumentNullException(nameof(value));
        SetString(key,JsonSerializer.Serialize(new{version,data=value},JsonOptions));
    }
    /// <summary>不存在、损坏、版本不同或 data 无法转换时返回 false，绝不自动覆盖旧记录；过期句柄仍抛异常。</summary>
    public bool TryGetJson<T>(string key,int expectedVersion,out T? value)
    {
        if(expectedVersion<1)throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        value=default;
        if(!TryGetString(key,out var text)||Encoding.UTF8.GetByteCount(text)>MaxValueBytes)return false;
        try {
            using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=16});
            var row=doc.RootElement;
            if(row.ValueKind!=JsonValueKind.Object)return false;
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in row.EnumerateObject())if(field.Name is not ("version" or "data")||!keys.Add(field.Name))return false;
            if(!row.TryGetProperty("version",out var version)||!version.TryGetInt32(out var number)||number!=expectedVersion||
                !row.TryGetProperty("data",out var data)||data.ValueKind==JsonValueKind.Null)return false;
            var parsed=data.Deserialize<T>(JsonOptions);
            if(parsed is null)return false;
            value=parsed;return true;
        } catch(Exception e)when(e is JsonException or InvalidOperationException or NotSupportedException) { return false; }
    }
}
