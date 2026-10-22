namespace EZapi {
    public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
    internal static class GameSession {
        internal static readonly int Thread=Environment.CurrentManagedThreadId;
        internal static GameRunIdentity? Identity=new(1,"test",1);
        internal static void RequireThread() {if(Environment.CurrentManagedThreadId!=Thread)throw new InvalidOperationException("wrong thread");}
        internal static bool TryResolve(long? epoch,out object player) {player=new();return Environment.CurrentManagedThreadId==Thread&&Identity is {} run&&(!epoch.HasValue||epoch==run.Epoch);}
    }
}
namespace Il2Cpp {
    public class GameInventory { public IntPtr Pointer {get;set;}=new(10); }
    public class GameItem {
        public IntPtr Pointer {get;set;}=new(1);
        public int uniqueId {get;set;}=1;
        public int unitCount {get;set;}=1;
        public GameInventory? parentInventory {get;set;}=new();
        public string identifier="test.item";
        public bool Owned=true,LiquidContainer;
        public int Energy=100,MachineCost=7,Free=1000,Total=1000,Purity;
        public long Value=123;
        public Dictionary<string,TagState> Tags=new();
        public void Destroy()=>Pointer=IntPtr.Zero;
        public bool IsTag(string id)=>id=="LIQUID_CONTAINER_TAG"&&LiquidContainer;
        public TagState? GetTagReadonly(string id)=>Tags.GetValueOrDefault(id);
        public T? TryCast<T>() where T:class=>this as T;
        public long GetCurrentValue(bool retail,bool children,bool events,bool force,long extra) {
            if(retail||!children||!events||force||extra!=0)throw new Exception("wrong valuation flags");return Value;
        }
    }
    public sealed class TagState { public int Value;public int GetInt()=>Value; }
    public sealed class RichText {public string Text="";}
    public sealed class GameItemElement:GameItem {
        public RichText GetTooltip()=>new(){Text="normal"};
        public RichText GetTooltipAdvanced()=>new(){Text="advanced"};
    }
    public static class DirectoryMaster {
        public static readonly Dictionary<string,Func<GameItem>> Catalog=new();
        public static bool Has<T>(string id)=>Catalog.ContainsKey(id);
        public static GameItem? Item(string id,bool owned) {var item=Catalog[id]();item.Owned=owned;item.identifier=id;item.parentInventory=null;return item;}
    }
    public static class GeneralHelper {public static bool IsItemOwned(GameItem item)=>item.Owned; public static string GetDisplayNameFromID(string id)=>"Name:"+id;}
    public static class MachineryHelper {public static int GetMachinePowerUsage(GameItem item)=>item.MachineCost;}
    public static class PowerHelper {
        public static Func<GameItem,int,bool>? Draw;
        public static Action<GameItem,int>? Restore;
        public static bool Allow=true;
        public static int Draws,Restores;
        public static int GetAvailableEnergyFromItem(GameItem item,bool useBandwidth) {if(useBandwidth)throw new Exception("unexpected bandwidth limit");return item.Energy;}
        public static bool CanDrawPowerSource(GameItem item,int amount)=>Allow&&item.Energy>=amount;
        public static bool DrawPowerSource(GameItem item,int amount) {Draws++;if(Draw is {} draw)return draw(item,amount);item.Energy-=amount;return true;}
        public static void SetPowerSourceAt(GameItem item,int energy) {Restores++;if(Restore is {} restore)restore(item,energy);else item.Energy=energy;}
        public static void Reset(){Draw=null;Restore=null;Allow=true;Draws=Restores=0;}
    }
    public sealed class Liquid {
        public string identifier="water",currentPart="water_volume";
        public static List<Liquid>? Liquids=new(){new(),new(){identifier="salt",currentPart="salt_volume"}};
    }
    public static class WaterHelper {
        public static int GetFreeCapacity(GameItem item)=>item.Free;
        public static int GetTotalVolume(GameItem item)=>item.Total;
        public static int GetWaterPurity(GameItem item)=>item.Purity;
    }
    public sealed class ItemCondition {public string identifier="";}
    public static class WaterFeatureHelper {public static ItemCondition GetConditionFromPurity(int purity)=>new(){identifier=purity switch {3=>"pure",2=>"high",1=>"base",_=>"other"}};}
    public static class ItemConditionList {
        public static ItemCondition CreatePureWater()=>new(){identifier="pure"};
        public static ItemCondition CreateHighQualityWater()=>new(){identifier="high"};
        public static ItemCondition CreateBaseWater()=>new(){identifier="base"};
    }
}

namespace EZapi {internal static class NativeFactoryRegistry {internal static bool HasCreationError(string id)=>false;}}
