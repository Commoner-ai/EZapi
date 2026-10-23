namespace Il2CppInterop.Runtime { }
namespace Il2Cpp
{
    public class GraphNodeStorage
    {
        private static long _next=100;
        public IntPtr Pointer=new(Interlocked.Increment(ref _next));
        public List<GraphNodeStorage>? parents=new(),children=new();
        public T? TryCast<T>() where T:class=>this as T;
        public void Attach(GraphNodeStorage child){children!.Add(child);child.parents!.Add(this);if(child is GameItem item&&this is GameInventory inventory)item.parentInventory=inventory;}
    }
    public class GameInventory:GraphNodeStorage { }
    public sealed class TagState { public string? Value; public string? GetString()=>Value; }
    public sealed class ItemState { public IntPtr Pointer=new(8); }
    public sealed class PixelWindow { public bool isLocked=true; }
    public sealed class GameItem:GraphNodeStorage
    {
        private static int _identity;
        public int uniqueId=Interlocked.Increment(ref _identity),unitCount=1;
        public string identifier="none";public bool Owned,Frozen;public ItemState? state=new();public GameInventory? parentInventory;
        public PixelWindow? contentWindow;public Dictionary<string,TagState> Tags=new();
        public Action? OnSync,OnDestroy,OnOwned,OnSetOwned;public int DestroyCalls,SyncCalls;
        public TagState? GetTagReadonly(string key)=>Tags.GetValueOrDefault(key);
        public void Destroy(){DestroyCalls++;OnDestroy?.Invoke();}
        public void SyncModifiedState(){SyncCalls++;OnSync?.Invoke();}
    }
    public sealed class PlayerStore
    {
        public GameInventory? gridInv=new();public Dictionary<string,GameInventory>? saveBags=new();
    }
    public static class DirectoryMaster { public static bool HasItem=true;public static bool Has<T>(string id)=>HasItem; }
    public static class GeneralHelper
    {
        public static bool IgnoreOwnership;
        public static bool IsItemOwned(GameItem item){item.OnOwned?.Invoke();return item.Owned;}
        public static void SetItemOwned(GameItem item,bool owned=true){if(!IgnoreOwnership)item.Owned=owned;item.OnSetOwned?.Invoke();}
    }
    public static class LockHelper
    {
        public static int Loads;public static bool Ignore;public static Action<GameItem>? OnLoad;
        public static void LoadLockedContainer(GameItem item){Loads++;if(!Ignore&&item.contentWindow is {} window)window.isLocked=true;OnLoad?.Invoke(item);}
        public static bool IsCurrentlyLocked(GameItem item)=>item.contentWindow?.isLocked==true;
    }
    public static class PreBuiltItemHelper
    {
        public static int Calls;public static string? Last;public static Func<string,GameItem>? Override;public static GameItem? Created;
        public static GameItem Make(string method)
        {
            Calls++;Last=method;if(Override is {} f)return Created=f(method);
            var d=EZapi.PreparedItemApi.Describe().Single(d=>d.NativeFactory==method);
            var item=new GameItem{identifier=d.ItemId};
            if(d.LootRecipe is {} recipe){item.Tags.Add("LOOT_CRATE_ID_STRING",new(){Value=recipe});item.contentWindow=new();var bag=new GameInventory();item.Attach(bag);bag.Attach(new GameItem{identifier="base.loot"});}
            return Created=item;
        }
        public static GameItem LootCrateSecurity()=>Make(nameof(LootCrateSecurity));
        public static GameItem LootCrateMedical()=>Make(nameof(LootCrateMedical));
        public static GameItem LootCrateEngineering()=>Make(nameof(LootCrateEngineering));
        public static GameItem LootCrateService()=>Make(nameof(LootCrateService));
        public static GameItem LootCrateEvidence()=>Make(nameof(LootCrateEvidence));
        public static GameItem RandomPerformanceModule()=>Make(nameof(RandomPerformanceModule));
        public static GameItem RandomEfficiencyModule()=>Make(nameof(RandomEfficiencyModule));
        public static GameItem RandomQualityModule()=>Make(nameof(RandomQualityModule));
        public static GameItem RandomOverclockModule()=>Make(nameof(RandomOverclockModule));
        public static GameItem RandomEcoModule()=>Make(nameof(RandomEcoModule));
        public static GameItem RandomFinenessModule()=>Make(nameof(RandomFinenessModule));
        public static GameItem CreateRealGenuineInjector()=>Make(nameof(CreateRealGenuineInjector));
    }
}
namespace EZapi
{
    internal static class GameSession
    {
        internal static bool OnThread=true;internal static GameRunIdentity? Run=new(560,"test",1);internal static Il2Cpp.PlayerStore Player=new();
        internal static Il2Cpp.PlayerStore Resolve(GameRunIdentity run)=>Run==run?Player:throw new Exception("stale");
    }
    internal static class NativeAccess
    {
        internal static GameRunIdentity RequireRun()=>GameSession.OnThread&&GameSession.Run is {} run?run:throw new Exception("unloaded");
        internal static bool SameRun(GameRunIdentity run)=>GameSession.OnThread&&GameSession.Run==run;
        internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item)=>item is not null&&item.Pointer!=IntPtr.Zero&&item.unitCount>0?item:throw new Exception("dead");
    }
    internal static class GameBuildFingerprint { internal static bool IsKnown=true; }
    public static class InventoryFreezeApi
    { public static bool IsAvailable=true;public static Action<Il2Cpp.GameItem>? OnFreeze;public static bool IsFrozen(Il2Cpp.GameItem item){OnFreeze?.Invoke(item);return item.Frozen;} }
    public static class InventoryApi
    { public static bool IsItemDragActive; public static List<Il2Cpp.GameItem> Items=new();public static Action? OnCapture;public static IReadOnlyList<Il2Cpp.GameItem> CaptureRunItems(Il2Cpp.PlayerStore player){OnCapture?.Invoke();return Items.ToArray();} }
}
