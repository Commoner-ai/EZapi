// Tests execute the real factory and parser. Native helpers only capture calls or inject failure;
// they do not simulate eating, hydration, freshness progression, liquid contents or persistence.
namespace Il2CppSystem {
    public sealed class Func<T1,T2,TResult> { public System.Func<T1,T2,TResult> Invoke=null!; }
}
namespace Il2CppInterop.Runtime {
    public static class DelegateSupport {
        public static T ConvertDelegate<T>(Delegate value) where T:new() {
            var result=new T();
            if(result is Il2CppSystem.Func<Il2Cpp.GameItem,Il2Cpp.GameItem,bool> fn)fn.Invoke=(Func<Il2Cpp.GameItem,Il2Cpp.GameItem,bool>)value;
            return result;
        }
    }
}
namespace Il2Cpp {
    public sealed class TagState {
        public bool Enabled; public string String="";public int Int;public long valueLong;public float Float;public double Double;public bool Bool;
        public void SetEnabled(bool x)=>Enabled=x;
        public void SetString(string x)=>String=x;
        public void SetInt(int x)=>Int=x;
        public void SetFloat(float x)=>Float=x;
        public void SetDouble(double x)=>Double=x;
        public void SetBool(bool x)=>Bool=x;
    }
    public sealed class TagSystem {
        public Dictionary<string,TagState> Tags=new();
        public void InitTagString(string id)=>Tags.TryAdd(id,new());
        public TagState GetTag(string id)=>Tags[id];
    }
    public sealed record GridShape(int Width,int Height,string Data);
    public sealed class GridShapeBuilder {
        int w,h;string data="";
        public GridShapeBuilder(int w,int h)=>(this.w,this.h)=(w,h);
        public void SetData(string v,int width)=>data=v;
        public void SetDataFill(int v)=>data=new string('1',w*h);
        public GridShape Build()=>new(w,h,data);
    }
    public sealed class GameItem {
        public string identifier="",identifierName="",Name="",shortDescription="",longDescription="",flavorText="",customText="";
        public string? soundDragStart,soundDragEnd,soundContainerOpen,soundContainerClose,soundSelect,soundExamineOpen,soundToggle,soundActivate,soundUse;
        public bool forceDisableUse,forceDisableActivate,activateDefault,triggerOverwatch,canUseOutsideCombat;
        public long Value;public int Count,bonusAccuracy,DestroyCount,SyncCount;
        public TagSystem? state;
        public GridShape? Shape;
        public HashSet<string> Types=new();
        public Il2CppSystem.Func<GameItem,GameItem,bool>? mayStackItemFunc;
        public (int Calories,bool Canned)? Food;public bool Meat;public int? Freshness;public (int Capacity,bool StartFull)? Liquid;
        public void SetName(string x)=>Name=x;
        public void SetValue(long x)=>Value=x;
        public void SetShape(GridShape x)=>Shape=x;
        public void SetUnitCount(int x)=>Count=x;
        public void SetGameItemType(string x)=>Types.Add(x);
        public void SyncModifiedState()=>SyncCount++;
        public void Destroy()=>DestroyCount++;
    }
    public static class ItemDirectory {
        public static List<GameItem> Created=new();
        public static GameItem CreateEmptyItem(string id) {var i=new GameItem{identifier=id};Created.Add(i);return i;}
    }
    public static class FoodItemHelper {
        public static int Calls,FreshCalls;public static bool ThrowFood,ThrowFresh;
        public static void InitFoodItem(GameItem item,int calories,bool isCanned,bool isMeat=false) {
            Calls++;item.Food=(calories,isCanned);item.Meat=isMeat;item.state=new();item.state.InitTagString("native.food");
            // Native defaults are deliberately nonzero to detect accidental blanket overrides.
            item.activateDefault=true;item.canUseOutsideCombat=true;item.bonusAccuracy=7;item.forceDisableUse=true;
            if(ThrowFood)throw new InvalidOperationException("food failure");
        }
        public static void InitFreshFood(GameItem item,int freshness) {
            FreshCalls++;item.Freshness=freshness;if(ThrowFresh)throw new InvalidOperationException("freshness failure");
        }
    }
    public static class WaterHelper {
        public static void InitLiquidContainerItem(GameItem item,int capacity,int basePrice,bool filter,bool graduated,bool dropper,bool transparent,int page) {
            LiquidContainerHelper.InitLiquidContainerItem(item,capacity,false);
        }
        public static void FillWithBaseWater(GameItem item){item.Liquid=(item.Liquid!.Value.Capacity,true);}
    }
    public static class LiquidContainerHelper {
        public static int Calls;public static bool Throw;
        public static void InitLiquidContainerItem(GameItem item,int capacity=500,bool isStartFull=true) {
            Calls++;item.Liquid=(capacity,isStartFull);item.activateDefault=true;item.canUseOutsideCombat=true;
            item.state=new();item.state.InitTagString("native.liquid");
            if(Throw)throw new InvalidOperationException("liquid failure");
        }
    }
    public static class UseCountHelper {
        public static int Calls;
        public static void InitUseCountItem(GameItem i,int a,bool b,bool c,bool d,int e,int f)=>Calls++;
        public static void SetCurrentUseCount(GameItem i,int n) { }
    }
    public static class ModuleHelper {public static void InitModuleItem(GameItem i,string type,int performance,int efficiency,int quality,bool ship,string effect,int tier) { }}
    public static class ModuleEffectHelper {public static void InitNode(GameItem i) { }}
    public static class ContrabandHelper {public static void InitContrabandItem(GameItem i,int n) { } }
}
namespace EZapi {
    internal static class NativeContainerAdapter {
        internal static void Initialize(Il2Cpp.GameItem item,ItemDefinition definition) {
            if(definition.Container is not null)throw new NotSupportedException("Container behavior is covered by the dedicated container suite.");
        }
    }
    internal static class ItemActivation {internal static void Bind(Il2Cpp.GameItem i,ItemDefinition d) { } }
    internal static class LegacySpriteAdapter {internal static void Bind(Il2Cpp.GameItem i,ItemDefinition d) { } }
    internal static class ItemRegistry {
        internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> d)=>Array.Empty<ItemRegistrationResult>();
    }
}
