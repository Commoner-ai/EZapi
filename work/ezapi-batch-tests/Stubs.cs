namespace MelonLoader { public static class MelonLogger { public static void Msg(string s) {} public static void Error(string s) {} } }
namespace Il2Cpp {
 public sealed class GameGridInventory {public IntPtr Pointer=new(1);}
 public sealed class EmporiumEntry { public static EmporiumEntry? Instance=new();public GameGridInventory? invElement=new(); }
 public sealed class Canvas { public bool activeInHierarchy=true; }
 public sealed class StoreUIManager {public static StoreUIManager? Instance=new();public Canvas? inventoryCanvas=new();}
}
namespace EZapi {
 public sealed record GameRunIdentity(int Slot,string Run,long Epoch);
 internal static class GameSession {internal static GameRunIdentity? Identity=new(1,"test",1);}
 internal static class NativeAccess {internal static bool SameRun(GameRunIdentity run)=>GameSession.Identity==run;}
 internal static class InventoryApi {internal static bool IsItemDragActive;}
 internal static class ItemRegistry {internal static int Count=10;internal static IReadOnlyList<int> AllDefinitions()=>Enumerable.Range(0,Count).ToArray();}
 internal static class EZapiBootstrap {internal static int Writes;internal static bool WriteDiagnostics(){Writes++;return true;}}
 internal static class Calls {internal static readonly List<string> Order=new();internal static string? ThrowAt;internal static string Add(string name){Order.Add(name);if(ThrowAt==name)throw new InvalidOperationException("injected");return "report "+name;}}
 internal static class DebugLifecycle {internal static bool Recording;internal static string Toggle(){Recording=!Recording;return Calls.Add("lifecycle");}}
 internal static class ItemDiagnostics {internal static string Run()=>Calls.Add("items");}
 internal static class DebugInventory {internal static string Run(GameRunIdentity? run)=>Calls.Add("inventory");}
 internal static class DebugResources {internal static string Run(GameRunIdentity? run)=>Calls.Add("resources");}
 internal static class DebugProduction {internal static string Run()=>Calls.Add("production");}
}
