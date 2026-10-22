namespace Il2CppSystem {
 public class Action<T> {
  public System.Action<T> Handler=null!;
  public IntPtr Pointer {get;}=new(System.Threading.Interlocked.Increment(ref Next));static int Next;
  public System.Collections.Generic.List<Action<T>> GetInvocationList()=>new(){this};
 }
}
namespace Il2CppSystem.Collections.Generic {
 public class List<T>:System.Collections.Generic.List<T> {
  public bool FailAdd,FailAfterAdd;
  public new void Add(T value){if(FailAdd)throw new Exception("before add");base.Add(value);if(FailAfterAdd)throw new Exception("after add");}
 }
}
namespace Il2CppInterop.Runtime {
 public static class DelegateSupport {
  public static T ConvertDelegate<T>(Delegate value) where T:new() {var result=new T();result!.GetType().GetField("Handler")!.SetValue(result,value);return result;}
 }
}
namespace Il2Cpp {
 public sealed class GameInventory {
 static int next=9000;public IntPtr Pointer=new(++next);public Il2CppSystem.Collections.Generic.List<GameItem> childItems=new();
}
 public sealed class GameItem {
  static int next;public IntPtr Pointer=new(++next);public string identifier="";public int unitCount=1;public GameInventory? parentInventory;public bool Owned,Destroyed;
  public void Destroy(){if(Destroyed)throw new Exception("double destroy");Destroyed=true;}
 }
 public sealed class PlayerStore {
 public Il2CppSystem.Collections.Generic.List<GameItem> Items=new();public Il2CppSystem.Collections.Generic.List<GameItem> FindAllItem(bool all)=>Items;
 public bool Reject,ThrowBefore,ThrowAfter,Merge;public System.Action? OnPlace;
 public void AddDirectSellingItemToTable(GameItem item,bool owned=false,bool stolen=false,bool ignore=false,int heat=0){
  if(ThrowBefore)throw new Exception("placement before");
  if(Reject){item.Destroy();return;}
  var target=EmporiumEntry.Instance!.frontInvinvElement!;
  var existing=Merge?target.childItems.FirstOrDefault(i=>i.identifier==item.identifier):null;
  if(existing is not null){existing.unitCount+=item.unitCount;item.Destroy();}
  else{item.Owned=owned;item.parentInventory=target;target.childItems.Add(item);}
  OnPlace?.Invoke();if(ThrowAfter)throw new Exception("placement after");
 }
}
public sealed class EmporiumEntry {public static EmporiumEntry? Instance=new();public GameInventory? frontInvinvElement=new();}
 public static class StoreClientList {public static void PlaceInventorInventory(bool visiting=false) {}}
 public static class GeneralHelper {public static bool IsItemOwned(GameItem i)=>i.Owned;}
 public static class ModHook {
  public static Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<GameItem>>? OnPlaceInventorInventoryItemLate;
  public static void add_OnPlaceInventorInventoryItemLate(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<GameItem>> a)=>OnPlaceInventorInventoryItemLate=a;
  public static void remove_OnPlaceInventorInventoryItemLate(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<GameItem>> a){if(OnPlaceInventorInventoryItemLate==a)OnPlaceInventorInventoryItemLate=null;}
  public static void FireOnPlaceInventorInventoryItemLate(Il2CppSystem.Collections.Generic.List<GameItem> list)=>OnPlaceInventorInventoryItemLate?.Handler(list);
 }
}
namespace MelonLoader {public static class MelonLogger {public static void Error(string s){}public static void Warning(string s){}}}
namespace EZapi {
 public sealed record NativeHookStatus(bool Ready);
 internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Finalizer=null);
 internal sealed class NativeHookGroup {
  public static bool Live=true;internal bool Ready;internal NativeHookGroup(string id){}
  internal void Install(Type t,params NativeHookSpec[] specs){Ready=true;}
  internal NativeHookStatus Snapshot()=>new(Ready&&Live);
  internal void Stop()=>Ready=false;
 }
 public sealed record GameRunIdentity(int Slot);
 internal static class GameSession {
  internal static readonly int Thread=Environment.CurrentManagedThreadId;
  internal static bool OnThread=>Environment.CurrentManagedThreadId==Thread;
  internal static GameRunIdentity? Identity=new(533);internal static Il2Cpp.PlayerStore Player=new();
  internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}
  internal static Il2Cpp.PlayerStore Resolve(GameRunIdentity run)=>Player;
 }
 internal static class NativeAccess {
  internal static GameRunIdentity RequireRun(){GameSession.RequireThread();if(GameSession.Identity is null)throw new InvalidOperationException("no run");return GameSession.Identity;}
  internal static bool SameRun(GameRunIdentity run)=>GameSession.Identity==run;
 }
 internal static class NativeFactoryRegistry {
  internal static readonly System.Collections.Generic.List<(string Id,NightShopStockPolicy Policy,Func<bool>? Available)> Declarations=new();
  internal static IEnumerable<(string Id,NightShopStockPolicy Policy,Func<bool>? Available)> StockDeclarations()=>Declarations.ToArray();
 }
 internal sealed record Definition(string Id,string NightShop);
 internal static class ItemRegistry {
  internal static readonly System.Collections.Generic.List<Definition> Definitions=new();
  internal static IEnumerable<Definition> AllDefinitions()=>Definitions;
  internal static readonly HashSet<string> Unapplied=new();
  internal static string GetStatus(string id)=>Unapplied.Contains(id)?"Failed":"Applied";
 }
 internal static class Items {
  internal static readonly System.Collections.Generic.List<Il2Cpp.GameItem> Created=new();
  internal static System.Action? OnCreate;internal static bool Fail;
  internal static Il2Cpp.GameItem Create(string id,bool owned){if(Fail)throw new Exception("factory");OnCreate?.Invoke();var i=new Il2Cpp.GameItem{identifier=id,Owned=owned};Created.Add(i);return i;}
 }
}
