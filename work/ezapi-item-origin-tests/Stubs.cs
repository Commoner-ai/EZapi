namespace EZapi
{
    internal static class GameSession { internal static int Thread = Environment.CurrentManagedThreadId; internal static bool OnThread => Environment.CurrentManagedThreadId == Thread; internal static GameRunIdentity? Run = new(5,"test",1); }
    internal static class NativeAccess
    {
        internal static GameRunIdentity RequireRun() => GameSession.OnThread && GameSession.Run is {} run ? run : throw new InvalidOperationException();
        internal static bool SameRun(GameRunIdentity run) => GameSession.OnThread && GameSession.Run == run;
        internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item) => item is not null && item.Pointer != IntPtr.Zero && item.unitCount > 0 ? item : throw new InvalidOperationException();
    }
    internal static class GameBuildFingerprint { internal static bool IsKnown = true; }
    public static class InventoryFreezeApi { public static bool IsAvailable = true; public static bool IsFrozen(Il2Cpp.GameItem item) { item.OnFreeze?.Invoke(); return item.Frozen; } }
    public static class InventoryApi { public static bool IsItemDragActive; }
}
namespace Il2Cpp
{
    public class NativeObject { public IntPtr Pointer = new(1); }
    public class ItemState : NativeObject { public NativeObject? dict = new(); }
    public class GameItem : NativeObject
    {
        public int uniqueId=42, unitCount=1; public string identifier="example.item";
        public NativeObject? parentInventory; public ItemState? state = new();
        public bool Stolen, Owned=true, Frozen, NativeLowValue; public int Heat, Writes, Syncs, ChanceReads;
        public Action? OnFreeze, OnStolenRead, OnChanceRead, OnWrite, OnSync, OnOwned;
        public void SyncModifiedState() { Syncs++; OnSync?.Invoke(); }
    }
    public static class GeneralHelper { public static bool IsItemOwned(GameItem item) { item.OnOwned?.Invoke(); return item.Owned; } }
    public static class StolenHelper
    {
        public static bool IsStolenItem(GameItem item) { item.OnStolenRead?.Invoke(); return item.Stolen; }
        public static int GetFoundChance(GameItem item) { item.ChanceReads++; item.OnChanceRead?.Invoke(); return item.Heat; }
        public static void InitStolenItem(GameItem item, int forcedHeat=0) { item.Writes++; if (!item.Stolen) { item.Stolen=forcedHeat>0 || !item.NativeLowValue; item.Heat=forcedHeat==0 ? 37 : forcedHeat; } item.OnWrite?.Invoke(); }
        public static void SetHeat(GameItem item,int heat) { item.Writes++; item.Stolen=true; item.Heat=heat; item.OnWrite?.Invoke(); }
        public static void RemoveStolenStatus(GameItem item) { item.Writes++; item.Stolen=false; item.Heat=0; item.OnWrite?.Invoke(); }
    }
}
