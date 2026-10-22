// Real EZapi orchestration/adapter sources under test. Native allocation, journal replay, RNG and gameplay are not simulated.
namespace Il2CppSystem {
    public class Func<T> { }
    public class Action { public System.Action Value=null!;public void Invoke()=>Value(); }
}
namespace Il2CppSystem.Collections.Generic {
    public interface IEnumerable<T>:System.Collections.Generic.IEnumerable<T> { }
    public class List<T>:System.Collections.Generic.List<T>,IEnumerable<T> {
        public TInterface Cast<TInterface>()=>(TInterface)(object)this;
    }
    public class Dictionary<K,V>:System.Collections.Generic.Dictionary<K,V> where K:notnull { }
}
namespace Il2CppInterop.Runtime {
    public static class DelegateSupport {
        public static T ConvertDelegate<T>(Delegate value) where T:new() {
            var result=new T();if(result is Il2CppSystem.Action action)action.Value=(System.Action)value;return result;
        }
    }
}
namespace Il2Cpp {
    public class GameItem { }
    public class ModItemDirectory {
        private static int next;
        public IntPtr Pointer {get;init;}=new(++next);
        public HashSet<string> Keys=new();
        public bool Has(string id)=>Keys.Contains(id);
        public bool Add(string id,Il2CppSystem.Func<GameItem> factory)=>Keys.Add(id);
    }
    public static class ModHook {
        public static readonly System.Collections.Generic.List<Il2CppSystem.Action> Handlers=new();
        public static bool ThrowAfterAdd,ThrowOnRemove;
        public static void add_OnLootTablesLoaded(Il2CppSystem.Action action) { Handlers.Add(action);if(ThrowAfterAdd)throw new Exception("partial add"); }
        public static void remove_OnLootTablesLoaded(Il2CppSystem.Action action) { if(ThrowOnRemove)throw new Exception("remove failure");Handlers.Remove(action); }
        public static void Fire() {foreach(var action in Handlers.ToArray())action.Invoke();}
    }
    public class LootEntry {
        public LootEntry(string id,float weight,string? owner=null)=>(this.id,this.weight,sourceModId)=(id,weight,owner);
        public string id;public float weight;public string? sourceModId,modifiedByModId;
    }
    public static class LootRegistry {
        public static Il2CppSystem.Collections.Generic.Dictionary<string,Il2CppSystem.Collections.Generic.List<LootEntry>> tables=new(),groups=new();
        public static readonly System.Collections.Generic.List<string> Calls=new();
        public static int ThrowAfterCall,RollCount;
        public static void RebuildIfDirty() { }
        static Il2CppSystem.Collections.Generic.Dictionary<string,Il2CppSystem.Collections.Generic.List<LootEntry>> Lists(bool group)=>group?groups:tables;
        static void Done(string name) {Calls.Add(name);if(Calls.Count==ThrowAfterCall)throw new Exception("native partial failure");}
        static void Register(string owner,string id,Il2CppSystem.Collections.Generic.IEnumerable<LootEntry> entries,bool group) {
            var list=new Il2CppSystem.Collections.Generic.List<LootEntry>();list.AddRange(entries);
            Lists(group).Add(id,list);Done((group?"registerGroup:":"registerTable:")+id);
        }
        public static void RegisterTable(string o,string id,Il2CppSystem.Collections.Generic.IEnumerable<LootEntry> entries)=>Register(o,id,entries,false);
        public static void RegisterGroup(string o,string id,Il2CppSystem.Collections.Generic.IEnumerable<LootEntry> entries)=>Register(o,id,entries,true);
        static void Add(string owner,string id,string entry,float weight,bool group) {
            var list=Lists(group)[id];var existing=list.FirstOrDefault(r=>r.id==entry);
            if(existing is null)list.Add(new(entry,weight,owner));else {existing.weight=weight;existing.modifiedByModId=owner;}
            Done((group?"addGroup:":"add:")+entry);
        }
        public static void AddEntry(string o,string id,string e,float w)=>Add(o,id,e,w,false);
        public static void AddGroupEntry(string o,string id,string e,float w)=>Add(o,id,e,w,true);
        static void Clear(string o,string id,bool group) {Lists(group)[id].Clear();Done(group?"clearGroup":"clear");}
        public static void ClearTable(string o,string id)=>Clear(o,id,false);
        public static void ClearGroup(string o,string id)=>Clear(o,id,true);
        static void Remove(string o,string id,string e,bool group) {Lists(group)[id].RemoveAll(r=>r.id==e);Done((group?"removeGroup:":"remove:")+e);}
        public static void RemoveEntry(string o,string id,string e)=>Remove(o,id,e,false);
        public static void RemoveGroupEntry(string o,string id,string e)=>Remove(o,id,e,true);
        static void Set(string o,string id,string e,float w,bool group) {
            var row=Lists(group)[id].FirstOrDefault(r=>r.id==e);if(row is not null){row.weight=w;row.modifiedByModId=o;}
            Done((group?"setGroup:":"set:")+e);
        }
        public static void SetWeight(string o,string id,string e,float w)=>Set(o,id,e,w,false);
        public static void SetGroupWeight(string o,string id,string e,float w)=>Set(o,id,e,w,true);
        public static string Roll(string id) {RollCount++;return tables[id].FirstOrDefault(r=>r.weight>0)?.id??"";}
        public static string RollGroup(string id)=>Roll(groups[id].First(r=>r.weight>0).id);
        public static void Reset() { tables.Clear();groups.Clear();Calls.Clear();ThrowAfterCall=0;RollCount=0; }
    }
}
namespace MelonLoader {
    public static class MelonLogger {
        public static readonly List<string> Errors=new();
        public static void Msg(string value) { }
        public static void Warning(string value) { }
        public static void Error(string value)=>Errors.Add(value);
    }
}
namespace EZapi {
    internal static class ItemActivation {internal static void Clear() { } }
    internal static class OfficialHookSubscriptions {internal static void Start() { }internal static void Stop() { } }
    internal static class NativeItemFactory {internal static Il2Cpp.GameItem Create(ItemDefinition d)=>new();}
    internal static class LegacySpriteAdapter {
        internal static string? Prepare(ItemDefinition d,string? folder) {
            if(d.Sprite=="missing")throw new InvalidDataException("missing PNG");return folder;
        }
        internal static void Commit(ItemDefinition d,string? path) { }
        internal static string? SourcePath(ItemDefinition d)=>null;
    }
    internal static class EZapiBootstrap {internal static bool WriteDiagnostics()=>true;}
    internal static class GameSession {
        internal static int Thread=Environment.CurrentManagedThreadId;
        internal static void RequireThread() {if(Thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("EZAPI_RUN_THREAD");}
    }
    internal static class RunData {
        internal static void ValidateModId(string owner) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(owner,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))throw new ArgumentException("EZAPI_RUN_MOD_ID");
        }
    }
    internal static class PresentationApi { internal static string? ResolveOwnerDisplayName(string ownerId,string locale)=>null; }
}
