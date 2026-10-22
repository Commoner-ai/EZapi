// 图日记链接生产源码；游戏图节点/移除/标签/冻结为显式离线替身。
namespace Il2Cpp {
 public class GraphNodeStorage {static long next;public IntPtr Pointer=new(++next);public List<GraphNodeStorage> children=new();public T Cast<T>() where T:class=>this as T??throw new InvalidCastException();public T? TryCast<T>() where T:class=>this as T;}
 public sealed class NativeDict {static long next;public IntPtr Pointer=new(++next);public int Count=1;}
 public sealed class TagSystem {static long next;public IntPtr Pointer=new(++next);public NativeDict dict=new();public int Value;}
 public class GameInventory:GraphNodeStorage {public GameItem Owner;public string Id;public bool Locked;public List<GameItem> childItems=new();public GameInventory(GameItem owner,string id){Owner=owner;Id=id;owner.children.Add(this);}public void Add(GameItem item){item.parentInventory?.Remove(item);childItems.Add(item);children.Add(item);item.parentInventory=this;}public void Remove(GameItem i){childItems.Remove(i);children.Remove(i);i.parentInventory=null;}}
 public class GameItem:GraphNodeStorage {public string identifier="fixture";public int uniqueId,unitCount=1;public bool Owned=true,Frozen;public long unitValue=10;public GameInventory? parentInventory;public GraphNodeStorage? contentWindow;public TagSystem state=new();public string Shape="original";public bool MayRemove()=>parentInventory is not null&&!Frozen&&!parentInventory.Locked;public int MaxNumRemove()=>MayRemove()?unitCount:0;public void SyncModifiedState(){}public static List<int> DestroyOrder=new();public static Action<GameItem>? OnDestroy;public void Destroy(){if(Pointer==IntPtr.Zero)throw new Exception("access after destroy");if(children.SelectMany(n=>n.children).OfType<GameItem>().Any())throw new Exception("parent destroyed before children");DestroyOrder.Add(uniqueId);OnDestroy?.Invoke(this);Pointer=IntPtr.Zero;}}
}
namespace EZapi {
 public sealed record GameRunIdentity(int Epoch);
 internal static class NativeAccess {internal static GameRunIdentity Current=new(1);internal static GameRunIdentity RequireRun()=>Current;internal static bool SameRun(GameRunIdentity run)=>Current==run;}
 public sealed record NativeInventoryItem(long NativeIdentity,int InstanceId,string ItemId,int Units,bool PlayerOwned,string? Shape);
 public sealed record NativeInventorySnapshot(IntPtr Pointer,int[] Children,bool Locked,IReadOnlyList<NativeInventoryItem> Items);
 internal static class InventoryChecks {
  internal static NativeInventoryItem Item(Il2Cpp.GameItem item,bool includeGeometry=true){if(item.Pointer==IntPtr.Zero)throw new Exception("dead wrapper");return new(item.Pointer.ToInt64(),item.uniqueId,item.identifier,item.unitCount,item.Owned,includeGeometry?item.Shape:null);}
  internal static NativeInventorySnapshot Read(Il2Cpp.GameInventory inv,GameRunIdentity run)=>new(inv.Pointer,inv.childItems.Select(i=>i.uniqueId).ToArray(),inv.Locked,inv.childItems.Select(i=>Item(i)).ToArray());
  internal static bool Equal(NativeInventorySnapshot a,NativeInventorySnapshot b)=>a.Pointer==b.Pointer&&a.Locked==b.Locked&&a.Items.SequenceEqual(b.Items);
 }
 internal sealed class NativeTagBackup {readonly int v;NativeTagBackup(int value)=>v=value;internal static NativeTagBackup Capture(Il2Cpp.TagSystem tags)=>new(tags.Value);internal bool Matches(Il2Cpp.TagSystem tags)=>tags.Value==v;internal void Restore(Il2Cpp.TagSystem tags)=>tags.Value=v;}
 internal static class InventoryFreezeApi {internal static bool Refuse;internal static bool IsFrozen(Il2Cpp.GameItem item)=>item.Frozen;internal static bool Expel(IDisposable freeze,Il2Cpp.GameItem item,Il2Cpp.GameInventory inv){if(Refuse)return false;inv.Remove(item);return true;}}
 internal static class SaveGraphApi {
  internal static IReadOnlyList<Il2Cpp.GameItem> ItemsForObservation(Il2Cpp.GameItem root)=>Items(root);
  internal static IReadOnlyList<Il2Cpp.GameItem> Items(Il2Cpp.GameItem root){var q=new Queue<Il2Cpp.GraphNodeStorage>();q.Enqueue(root);var seen=new HashSet<IntPtr>();var result=new List<Il2Cpp.GameItem>();while(q.Count>0){var n=q.Dequeue();if(n.Pointer==IntPtr.Zero)throw new Exception("dead node");if(!seen.Add(n.Pointer))continue;if(n is Il2Cpp.GameItem item)result.Add(item);foreach(var c in n.children)q.Enqueue(c);}return result;}
 }
}
namespace Il2Cpp {public static class GeneralHelper {public static bool IsItemOwned(GameItem item)=>item.Owned;}}
