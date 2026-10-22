namespace EZapi {
    internal static class GameSession {
        internal static readonly int Thread=Environment.CurrentManagedThreadId;
        internal static bool OnThread=>Environment.CurrentManagedThreadId==Thread;
        internal static GameRunIdentity Identity=new(1,"run",1);
        internal static Il2Cpp.PlayerStore Resolve(GameRunIdentity run)=>Il2Cpp.PlayerStore.instance;
    }
    internal static class RunData {internal static bool IsAvailable=true;}
    internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
    internal static class NativeAccess {
        internal static GameRunIdentity RequireRun()=>GameSession.OnThread&&RunData.IsAvailable?GameSession.Identity:throw new InvalidOperationException();
        internal static bool SameRun(GameRunIdentity run)=>GameSession.OnThread&&RunData.IsAvailable&&run==GameSession.Identity;
    }
    internal static class NativeItemLocalization {internal static string CurrentLocaleCode="en";}
}
namespace Il2Cpp {
    public sealed class PlayerStore {public static readonly PlayerStore instance=new();public int Cash=40,Reads;public int GetCash(){Reads++;return Cash;}}
    public static class StoreStation {public static int Day=6,Reads;public static int GetDayCounter(){Reads++;return Day;}}
    public sealed class NetworkUpgrade {
        public static readonly Dictionary<string,NetworkUpgrade> Items=new();public static Action? OnRead;public static int Reads;
        public IntPtr Pointer=(IntPtr)10;public string id="upgrade",Title="Upgrade";public int cost=20,cooldownCurrent=2,cooldownDuration=7;
        public bool isRepeatable=true,Unlocked,Ready=true,PrerequisitesMet=true,DemoLocked;
        public string Missing="";public List<string>? prerequisite=new(){"base"};
        public static NetworkUpgrade? GetUpgradeById(string id){Reads++;OnRead?.Invoke();return Items.GetValueOrDefault(id);}
        public string GetLocalizedTitle(){OnRead?.Invoke();return Title;}
        public int GetCost()=>cost;public bool IsUnlocked()=>Unlocked;public bool IsReady()=>Ready;
        public bool IsHavePrerequisite()=>PrerequisitesMet;public string GetMissingPrerequisite()=>Missing;
        public static bool IsLockedInDemo(string id)=>Items[id].DemoLocked;
    }
    public sealed class StoreService {
        public static readonly Dictionary<string,StoreService> Items=new();public static Action? OnRead;public static int Reads;
        public IntPtr Pointer=(IntPtr)20;public string id="service",displayName="Service";
        public int cost=9,currentCooldown=1,totalCooldown=5;public bool unlocked=true,introduced=true,isUsing,Active,OnCooldown=true,isMandatory;
        public static StoreService? FindServiceByID(string id){Reads++;OnRead?.Invoke();return Items.GetValueOrDefault(id);}
        public bool IsActive(){OnRead?.Invoke();return Active;}public bool IsOnCooldown()=>OnCooldown;
    }
}
