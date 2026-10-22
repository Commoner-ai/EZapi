namespace Il2CppSystem.Collections.Generic {
public sealed class Dictionary<TKey,TValue> where TKey:notnull {
    private static long _next=10000;
    public IntPtr Pointer {get;}=new(Interlocked.Increment(ref _next));
    private readonly System.Collections.Generic.Dictionary<TKey,TValue> _data=new();
    public Action<TKey,TValue>? AfterAdd; public Action<TKey>? BeforeAdd,BeforeRemove,AfterRemove;
    public int Count=>_data.Count;
    public TValue this[TKey key] {get=>_data[key];set=>_data[key]=value;}
    public bool ContainsKey(TKey key)=>_data.ContainsKey(key);
    public bool TryGetValue(TKey key,out TValue value)=>_data.TryGetValue(key,out value!);
    public void Add(TKey key,TValue value){BeforeAdd?.Invoke(key);_data.Add(key,value);AfterAdd?.Invoke(key,value);}
    public bool Remove(TKey key){BeforeRemove?.Invoke(key);var result=_data.Remove(key);AfterRemove?.Invoke(key);return result;}
}}
namespace Il2Cpp {
public sealed class GameItem {
    private static int _next;
    public GameItem(){uniqueId=Interlocked.Increment(ref _next);Pointer=new(uniqueId);}
    public IntPtr Pointer {get;set;}
    public int uniqueId {get;set;} public int unitCount=1;
    public long unitValue;public TagSystem? state=new();
    public Action? Sync;
    public void SyncModifiedState()=>Sync?.Invoke();
    public TagState? GetTagReadonly(string id)=>state?.dict?.TryGetValue(id,out var t)==true?t:null;
}
public sealed class TagSystem {public Il2CppSystem.Collections.Generic.Dictionary<string,TagState>? dict=new();}
public sealed class TagState {
    private static long _next=1000;
    public IntPtr Pointer {get;}=new(Interlocked.Increment(ref _next));
    public string valueString="";public bool Enabled;
    public TagState(string id,string name){}
    public TagState Enable(){Enabled=true;return this;}
    public TagState SetString(string v){valueString=v;return this;}
}
public static class ModuleEffectHelper {
    public sealed class ModuleEffect {
        private static long _next=5000;
        public IntPtr Pointer {get;set;}=new(Interlocked.Increment(ref _next));
        public string identifier="";
    }
    public static int Reads;
    private static Il2CppSystem.Collections.Generic.Dictionary<string,ModuleEffect>? _table=new();
    public static Il2CppSystem.Collections.Generic.Dictionary<string,ModuleEffect>? moduleEffects {get{Reads++;return _table;}set=>_table=value;}
    public static void InitRandomEffect(GameItem item,int count=-1){}
}
}
namespace MelonLoader {public static class MelonLogger {public static readonly List<string> Errors=new();public static void Error(string s)=>Errors.Add(s);}}
namespace EZapi {
public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
internal static class GameSession {
    internal static int MainThread=Environment.CurrentManagedThreadId;
    internal static GameRunIdentity? Identity=new(0,"test",1);
    internal static void RequireThread(){if(Environment.CurrentManagedThreadId!=MainThread)throw new InvalidOperationException("wrong thread");}
}
internal static class NativeAccess {
    internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem? item){GameSession.RequireThread();if(GameSession.Identity is null)throw new InvalidOperationException("run not loaded");if(item is null||item.Pointer==IntPtr.Zero||item.unitCount<=0)throw new ArgumentException("dead item");return item;}
    internal static bool SameRun(GameRunIdentity r)=>GameSession.Identity==r;
}
public sealed record LiquidPart(string Id,int Parts);
public sealed class LiquidSnapshot {
    public int CapacityParts {get;init;}
    public IReadOnlyList<LiquidPart> Contents {get;init;}=Array.Empty<LiquidPart>();
}
public static class RunData {internal static void ValidateModId(string id){if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))throw new ArgumentException("owner");}}
public sealed record NativeHookStatus(string OwnerId,bool Ready,IReadOnlyList<string> Targets,string? Error);
internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null,string? Finalizer=null);
internal sealed class NativeHookGroup {
    internal static bool Allow=true,Lost;
    internal static NativeHookSpec[] Specs=Array.Empty<NativeHookSpec>();
    private bool _ready;private readonly string _id;
    internal NativeHookGroup(string id)=>_id=id;
    internal void Install(Type callbacks,params NativeHookSpec[] specs){Specs=specs;_ready=Allow;}
    internal NativeHookStatus Snapshot()=>new(_id,_ready&&!Lost,Specs.Select(s=>s.Name).ToArray(),Allow?null:"missing");
    internal void Stop(){_ready=false;}
}
}
