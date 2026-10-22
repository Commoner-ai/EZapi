// Offline callback policy tests. These stubs do not simulate native allocation, UI or save behavior.
namespace Il2CppSystem {
    public abstract class NativeDelegate { public Delegate Value=null!; }
    public class Func<A,B,C,R>:NativeDelegate {
        public R Invoke(A a,B b,C c)=>((System.Func<A,B,C,R>)Value)(a,b,c);
    }
    public class Action<A,B,C>:NativeDelegate {
        public void Invoke(A a,B b,C c)=>((System.Action<A,B,C>)Value)(a,b,c);
    }
}
namespace Il2CppInterop.Runtime {
    public static class DelegateSupport {
        public static T ConvertDelegate<T>(Delegate value) where T:Il2CppSystem.NativeDelegate,new()=>new(){Value=value};
    }
}
namespace MelonLoader {
    public static class MelonLogger { public static List<string> Errors=new();public static void Error(string s)=>Errors.Add(s); }
}
namespace Il2Cpp {
    public class GameInventory { public IntPtr Pointer=new(10); }
    public class SlotMarker { }
    public class HealthData { public int woundState;public bool isWoundStable=true,isWoundedFresh; }
    public class PlayerStore {
        public static PlayerStore? instance=new();
        public int saveSlotId=0;public string runID="run";public HealthData? healthData=new();
    }
    public class GameItem {
        static int next=100;
        public IntPtr Pointer=new(++next);
        public string identifier="";public int unitCount=1;
        public bool forceDisableActivate,forceDisableUse,Owned=true;
        public int DestroyCount,Charges=3,UseCalls;public bool ThrowOnUse;
        public GameInventory? parentInventory;
        public Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,bool>? mayActivateSlotItemFunc,canActivateSlotItemFunc;
        public Il2CppSystem.Action<GameItem,GameInventory,SlotMarker>? onActivateSlotItemFunc;
        public void Destroy() { DestroyCount++;parentInventory=null; }
    }
    public static class GeneralHelper { public static bool IsItemOwned(GameItem item)=>item.Owned; }
    public static class UseCountHelper {
        public static bool CanUse(GameItem item,int charge)=>item.Charges>=charge;
        public static void Use(GameItem item,int charge) {
            if(item.ThrowOnUse)throw new InvalidOperationException("native use failure");
            item.UseCalls++;item.Charges-=charge;
        }
    }
}
namespace EZapi {
    internal static class EZapiBootstrap { internal static bool WriteDiagnostics()=>true; }
    internal static class ItemRegistry {
        internal static bool Ready=true;
        internal static readonly Dictionary<string,ItemDefinition> Definitions=new();
        internal static string? BehaviorRegistrationError(string id)=>!Ready?"EZAPI_TOO_LATE":
            !Definitions.ContainsKey(id)?"EZAPI_ACTIVATION_ITEM":Definitions[id].Activation is not null?"EZAPI_ACTIVATION_CONFLICT":null;
        public static string GetStatus(string id)=>Definitions.ContainsKey(id)?"Applied":"Unknown";
    }
}
