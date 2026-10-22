// Only the managed registration transaction is under test. These stand-ins do not simulate Unity or IL2CPP.
namespace Il2CppSystem {
    public class Func<T> { }
}
namespace Il2Cpp {
    public class GameItem { }
    public class ModItemDirectory {
        public IntPtr Pointer { get; init; }=new(1);
        public HashSet<string> Keys { get; }=new();
        public HashSet<string> FailIds=new();
        public int Adds { get; private set; }
        public bool Has(string id)=>Keys.Contains(id);
        public bool Add(string id,Il2CppSystem.Func<GameItem> factory) { Adds++;if(FailIds.Contains(id))return false;return Keys.Add(id); }
    }
}
namespace Il2CppInterop.Runtime {
    public static class DelegateSupport {
        public static T ConvertDelegate<T>(Delegate d) where T:new()=>new();
    }
}
namespace MelonLoader {
    public static class MelonLogger {
        public static void Msg(string s) { }
        public static void Warning(string s) { }
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
