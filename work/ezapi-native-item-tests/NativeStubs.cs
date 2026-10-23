// Only the managed registration transaction is under test. These stand-ins do not simulate Unity or IL2CPP.
namespace Il2CppSystem {
    public class Func<T> { public System.Func<T> Value=null!; public T Invoke()=>Value(); }
}
namespace Il2Cpp {
    public static class LocHelper {
        public static string Locale="en";public static bool Throw;public static int Reads;
        public static string? GetCurrentLocaleCode(){Reads++;if(Throw)throw new InvalidOperationException("stub locale not ready");return Locale;}
    }
    public class GameItem {
 private static int Next;public IntPtr Pointer {get;set;}=new(++Next);public int uniqueId=Next;public string identifier="",identifierName="",name="",shortDescription="",flavorText="",longDescription="",customText="";
 public PixelWindow? contentWindow;
 public string GetDisplayName(bool compact){EZapi.NativeHookGroup.Invoke(this,false);return name;}
 public void RestoreContentWindowPosition()=>EZapi.NativeHookGroup.Invoke(this,true);
 public int unitCount=1;public object? parentInventory;public HashSet<string> Types=new();public bool IsGameItemType(string type)=>Types.Contains(type);
 public bool Destroyed;public void Destroy()=>Destroyed=true;
 public T? TryCast<T>() where T:class=>this as T;public long GetCurrentValue(bool a,bool b,bool c,bool d,int e)=>1;
 public void SetName(string value)=>name=value;public void SyncModifiedState(){}
 }
 public class RichText {} public class GameItemElement:GameItem {public RichText GetTooltip()=>new();public RichText GetTooltipAdvanced()=>new();}
 public static class GeneralHelper {public static string GetDisplayNameFromID(string id)=>id;}
 public static class DirectoryMaster {
 public static HashSet<string> NativeIds=new();public static bool Has<T>(string id)=>NativeIds.Contains(id);
 public static System.Func<string,bool,GameItem?> Factory=(id,owned)=>null;public static GameItem? Item(string id,bool owned)=>Factory(id,owned);
 }
    public class ModItemDirectory {
        public IntPtr Pointer { get; init; }=new(1);
        public HashSet<string> Keys { get; }=new();
        public HashSet<string> FailIds=new();
        public int Adds { get; private set; }
        public Dictionary<string,Il2CppSystem.Func<GameItem>> Factories=new();
        public bool Has(string id)=>Keys.Contains(id);
        public bool Add(string id,Il2CppSystem.Func<GameItem> factory) { Adds++;if(FailIds.Contains(id))return false;if(!Keys.Add(id))return false;Factories.Add(id,factory);return true; }
    }
}
namespace Il2CppInterop.Runtime {
    public static class DelegateSupport {
        public static T ConvertDelegate<T>(Delegate d) where T:new() { var result=new T();if(result is Il2CppSystem.Func<Il2Cpp.GameItem> f)f.Value=(System.Func<Il2Cpp.GameItem>)d;return result; }
    }
}
namespace MelonLoader {
    public static class MelonLogger {
        public static void Msg(string s) { }
        public static List<string> Warnings=new();public static void Warning(string s) { Warnings.Add(s); }
        public static void Error(string s) { }
    }
}
namespace EZapi {
    internal sealed class PreparedLoot { }
    internal static class LootTables {
        internal static PreparedLoot? Prepare(string owner,IReadOnlyList<LootDefinition> definitions)=>null;
        internal static void Commit(PreparedLoot? batch) { }
        internal static void OnDirectoryReady() { }
    }
    internal static class ItemActivation { internal static void Clear() { } }
    internal static class OfficialHookSubscriptions {
        internal static void Start() { }
        internal static void Stop() { }
    }
    internal static class LegacySpriteAdapter {
        internal static string? Prepare(ItemDefinition d,string? folder) {
            if(d.Sprite=="missing")throw new InvalidDataException("missing PNG");
            return folder;
        }
        internal static void Commit(ItemDefinition d,string? path) { }
        internal static string? SourcePath(ItemDefinition d)=>null;
    }
    internal static class NativeItemFactory {
        internal static Il2Cpp.GameItem Create(ItemDefinition d)=>new();
    }
    internal static class EZapiBootstrap {
        internal static bool WriteDiagnostics()=>true;
    }
}

namespace EZapi {
 public sealed record ContentSubmissionResult(bool Accepted,string? Reason);

}

namespace EZapi {internal static class NativeAccess {internal static void RequireRun()=>GameSession.RequireThread();internal static void RequireItem(Il2Cpp.GameItem item)=>GameSession.RequireThread();}}
