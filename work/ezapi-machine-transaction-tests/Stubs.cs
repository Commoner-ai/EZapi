namespace Il2Cpp {
 public sealed class NativeDict:Dictionary<string,TagState> {private static int _next=50000;public IntPtr Pointer {get;}=new(++_next);}
 public sealed class TagSystem {public NativeDict dict=new();}
 public sealed class TagState {private static int _next=60000;public IntPtr Pointer {get;}=new(++_next);public string valueString="";public TagState(string id,string name){}public TagState Enable()=>this;public TagState SetString(string s){valueString=s;return this;}}
 public sealed class GameInventory {public IntPtr Pointer=new(70000);}
 public sealed class GameItem {
  private static int _next;public GameItem(){uniqueId=++_next;Pointer=new(uniqueId);}
  public IntPtr Pointer;public int uniqueId,unitCount=1;public string identifier="test.product";public bool Owned=true;
  public GameInventory? parentInventory;public long unitValue;public TagSystem state=new();public bool Destroyed;public int Destroys;
  public Action? OnDestroy;public void Destroy(){Destroys++;OnDestroy?.Invoke();Destroyed=true;unitCount=0;}
  public void SyncModifiedState(){}public TagState? GetTagReadonly(string key)=>state.dict.GetValueOrDefault(key);
 }
 public static class GeneralHelper {public static bool IsItemOwned(GameItem i)=>i.Owned;}
}
namespace EZapi {
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession {internal static readonly int Thread=Environment.CurrentManagedThreadId;internal static GameRunIdentity Identity=new(1,"test",1);internal static void RequireThread(){if(Thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("wrong thread");}}
 internal static class NativeAccess {internal static GameRunIdentity RequireRun(){GameSession.RequireThread();return GameSession.Identity;}internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item){RequireRun();if(item.Pointer==IntPtr.Zero||item.unitCount<1)throw new ArgumentException("dead item");return item;}internal static bool SameRun(GameRunIdentity identity)=>GameSession.Identity==identity;}
 public static class InventoryApi {public static bool IsItemDragActive;}
 public sealed record LiquidPart(string Id,int Parts);
 public sealed record LiquidSnapshot(int CapacityParts,IReadOnlyList<LiquidPart> Contents);
 public static class Items {
  public static Func<string,Il2Cpp.GameItem>? Factory;public static readonly List<Il2Cpp.GameItem> Made=new();
  public static Il2Cpp.GameItem Create(string id,bool owned){var item=Factory?.Invoke(id)??new(){identifier=id,Owned=owned};Made.Add(item);return item;}
 }
}
