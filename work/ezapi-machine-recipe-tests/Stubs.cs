namespace Il2Cpp {
 public sealed class PlayerStore {public IReadOnlyList<GameItem> LiveItems=Array.Empty<GameItem>();}
 public sealed class NativeDict:Dictionary<string,TagState> {public IntPtr Pointer {get;}=new(50000);}
 public sealed class TagSystem {private static int _next=80000;public IntPtr Pointer {get;}=new(++_next);public NativeDict dict=new();public TagState GetTag(string id){if(!dict.ContainsKey(id))dict.Add(id,new(id,id));return dict[id];}}
 public sealed class TagState {public IntPtr Pointer {get;}=new(60000);public string valueString="";public TagState(string id,string name){}public TagState Enable()=>this;public TagState SetString(string s){valueString=s;return this;}}
 public class GraphNodeStorage {private static int _next=100000;public IntPtr Pointer=new(++_next);public List<GraphNodeStorage> children=new();public T Cast<T>()where T:class=>this as T??throw new InvalidCastException();public T? TryCast<T>()where T:class=>this as T;}
 public sealed class PixelWindow:GraphNodeStorage {}
 public class GridShape{public int globalWidth=4,globalHeight=4;}
 public class GameInventory:GraphNodeStorage {
  public GameInventory(GameItem? owner=null){ParentItem=owner;owner?.children.Add(this);}public GameItem? ParentItem;public GameItem? GetParentItem()=>ParentItem;public List<GameItem> childItems=new();public bool InsertLocked,RemoveLocked;
  public bool IsInsertLocked()=>InsertLocked;public bool IsRemoveLocked()=>RemoveLocked;
  public void Add(GameItem i){i.parentInventory?.Expel(i);childItems.Add(i);children.Add(i);i.parentInventory=this;}public void Expel(GameItem i){childItems.Remove(i);children.Remove(i);i.parentInventory=null;}
 }
 public sealed class GameSlotInventory:GameInventory {public int defaultGridWidth=2,defaultGridHeight=2;}public sealed class GameGridInventory:GameInventory {public GridShape inventoryShape=new();}
 public sealed class GameItem:GraphNodeStorage {
  private static int _next;public GameItem(){uniqueId=++_next;children.Add(contentWindow!);}
  public string Shape="original";public int uniqueId,unitCount=1,Energy=100,NativeCost=7;public string identifier="test.product";public bool Owned=true,Frozen,May=true;public int Max=int.MaxValue;
  public PixelWindow? contentWindow=new();public GameInventory? parentInventory;public long unitValue=10;public TagSystem state=new();public bool Destroyed;
  private EZapi.MachineInventory? _layout;public EZapi.MachineInventory? Layout{get=>_layout;set{_layout=value;if(value is not null){foreach(var s in new GameInventory?[]{value.ItemInput,value.Output,value.Battery,value.Modules,value.Manual}.Where(s=>s is not null).Concat(value.LiquidInputs.Values)){s!.ParentItem=this;contentWindow!.children.Add(s);}}}}public HashSet<string> Tags=new();public EZapi.MachineLiquidSnapshot Water=new(1000000,new[]{new EZapi.MachineLiquidPart("water",500000)});
  public bool IsTag(string t)=>Tags.Contains(t);public bool MayRemove()=>May;public int MaxNumRemove()=>Max;
  public void Destroy(){Destroyed=true;unitCount=0;}public void SyncModifiedState(){}public TagState? GetTagReadonly(string key)=>state.dict.GetValueOrDefault(key);
 }
 public static class GeneralHelper {public static bool IsItemOwned(GameItem i)=>i.Owned;}
 public static class PowerHelper {public static bool CanDrawPowerSource(GameItem i,int cost)=>i.Energy>=cost;}
}
namespace EZapi {
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession {internal static readonly int Thread=Environment.CurrentManagedThreadId;internal static GameRunIdentity Identity=new(1,"test",1);internal static void RequireThread(){if(Thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("wrong thread");}}
 internal static class NativeAccess {internal static GameRunIdentity RequireRun(){GameSession.RequireThread();return GameSession.Identity;}internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item){RequireRun();if(item.Pointer==IntPtr.Zero||item.unitCount<1)throw new ArgumentException("dead item");return item;}internal static bool SameRun(GameRunIdentity identity)=>GameSession.Identity==identity;}
 public sealed record ContentSubmissionResult(bool Accepted,string? Reason);
 public static class InventoryApi {public static bool IsItemDragActive;}
 public static class InventoryFreezeApi {public static bool IsFrozen(Il2Cpp.GameItem i)=>i.Frozen;internal static bool Expel(IDisposable freeze,Il2Cpp.GameItem item,Il2Cpp.GameInventory inv){inv.Expel(item);return true;}}
 public sealed record InventoryPlacement(Il2Cpp.GameItem Item,Il2Cpp.GridShape Shape);
 public static class InventoryPlacementApi {public static IReadOnlyList<InventoryPlacement> PlanWholeGrid(Il2Cpp.GameGridInventory inv,IReadOnlyList<Il2Cpp.GameItem> items){if(inv.InsertLocked||inv.childItems.Count+items.Count>4)throw new InvalidOperationException("full output");return items.Select(i=>new InventoryPlacement(i,new())).ToArray();}}
 public sealed record MachineInventory(Il2Cpp.GameSlotInventory? Battery,Il2Cpp.GameInventory? Modules,Il2Cpp.GameInventory ItemInput,Il2Cpp.GameInventory Output,IReadOnlyDictionary<string,Il2Cpp.GameSlotInventory> LiquidInputs,Il2Cpp.GameSlotInventory? Manual){public bool RequiresBattery{get;init;}}
 public static class MachineLayoutApi {public static Func<Il2Cpp.GameItem,Il2Cpp.GameInventory,bool>? Admission;public static bool Allows(Il2Cpp.GameItem i,Il2Cpp.GameInventory inv)=>Admission?.Invoke(i,inv)??true;public static bool TryGetInventory(Il2Cpp.GameItem i,out MachineInventory s){s=i.Layout!;return s is not null;}}
 public sealed record NativeInventoryItem(long NativeIdentity,int InstanceId,string ItemId,int Units,bool PlayerOwned,string? Shape);
 public sealed record NativeInventorySnapshot(long Id,IReadOnlyList<NativeInventoryItem> Items,bool Insert,bool Remove);
 internal static class InventoryChecks {
  internal static NativeInventoryItem Item(Il2Cpp.GameItem item,bool includeGeometry=true){NativeAccess.RequireItem(item);return new(item.Pointer.ToInt64(),item.uniqueId,item.identifier,item.unitCount,item.Owned,includeGeometry?item.Shape:null);}
  internal static NativeInventorySnapshot Read(Il2Cpp.GameInventory i,GameRunIdentity r)=>new(i.Pointer.ToInt64(),i.childItems.Select(x=>Item(x)).ToArray(),i.InsertLocked,i.RemoveLocked);
  internal static bool Equal(NativeInventorySnapshot a,NativeInventorySnapshot b)=>a.Id==b.Id&&a.Insert==b.Insert&&a.Remove==b.Remove&&a.Items.SequenceEqual(b.Items);
 }
 internal sealed class NativeTagBackup {
  readonly Dictionary<string,(IntPtr Pointer,string Value)> tags;
  NativeTagBackup(Il2Cpp.TagSystem state)=>tags=state.dict.ToDictionary(p=>p.Key,p=>(p.Value.Pointer,p.Value.valueString));
  internal static NativeTagBackup Capture(Il2Cpp.TagSystem state)=>new(state);
  internal bool Matches(Il2Cpp.TagSystem state)=>tags.Count==state.dict.Count&&tags.All(p=>state.dict.TryGetValue(p.Key,out var t)&&t.Pointer==p.Value.Pointer&&t.valueString==p.Value.Value);
  internal void Restore(Il2Cpp.TagSystem state){state.dict.Clear();foreach(var p in tags)state.GetTag(p.Key).SetString(p.Value.Value);}
 }
 internal static class SaveGraphApi {
  internal static IReadOnlyList<Il2Cpp.GameItem> ItemsForObservation(Il2Cpp.GameItem root)=>Items(root);
  internal static IReadOnlyList<Il2Cpp.GameItem> Items(Il2Cpp.GameItem root){var q=new Queue<Il2Cpp.GraphNodeStorage>();q.Enqueue(root);var seen=new HashSet<IntPtr>();var items=new List<Il2Cpp.GameItem>();while(q.Count>0){var n=q.Dequeue();if(!seen.Add(n.Pointer))continue;if(n is Il2Cpp.GameItem item){NativeAccess.RequireItem(item);items.Add(item);}foreach(var child in n.children)q.Enqueue(child);}return items;}
 }
 public sealed record LiquidPart(string Id,int Parts);public sealed record LiquidSnapshot(int CapacityParts,IReadOnlyList<LiquidPart> Contents);
 public static class Liquids {public static MachineLiquidSnapshot CaptureProduction(Il2Cpp.GameItem i)=>i.Water;}
 internal static class LiquidMetadata {internal static bool Same(MachineLiquidSnapshot a,MachineLiquidSnapshot b)=>a.CapacityParts==b.CapacityParts&&a.Contents.SequenceEqual(b.Contents);}
 public static class Power {public static int GetEnergy(Il2Cpp.GameItem i)=>i.Energy;public static int GetMachineCost(Il2Cpp.GameItem i)=>i.NativeCost;}
 public static class Items {public static readonly List<Il2Cpp.GameItem> Made=new();public static Il2Cpp.GameItem Create(string id,bool owned){var i=new Il2Cpp.GameItem{identifier=id,Owned=owned};Made.Add(i);return i;}}
}

namespace EZapi {
 public static class InventoryAdmissionApi {
  public static Func<Il2Cpp.GameItem,Il2Cpp.GameGridInventory,bool>? Predicate;
  public static IDisposable Register(string owner,string item,string inventory,Func<Il2Cpp.GameItem,Il2Cpp.GameGridInventory,bool> allows){Predicate=allows;return new Lease();}
  sealed class Lease:IDisposable{public void Dispose(){Predicate=null;}}
 }
}

namespace EZapi {
 internal sealed class MachineAutomationContext {internal bool IsCurrent=>true;internal bool IsBlocked=>false;internal string? Selection=>null;internal void Complete(MachineBatchResult result,Func<bool> current){}internal void Close(){}}
 internal static class MachineAutomationApi {internal static MachineAutomationContext Create(Il2Cpp.GameItem m,MachineInventory s,Func<bool> current)=>new();internal static void Dispatch(string id,MachineAutomationPhase phase,MachineAutomationContext context){}}
 internal enum MachineAutomationPhase {BeforeBatch,AfterBatch}
 internal static class MachineFaultGuard {internal static bool Blocked(Il2Cpp.GameItem machine)=>false;internal static void Record(Il2Cpp.GameItem machine,MachineTransactionResult result){}}
}



namespace EZapi {public static class JsonItems {public static void ValidateObject(System.Text.Json.JsonElement e,Type t,string path){}}}
namespace EZapi {public sealed record MachineDefinition(string MachineId,MachineLayoutDefinition Template,IReadOnlyList<MachineRecipe> Recipes){public MachinePowerRule? Power{get;init;}public int ProductionMarkupPercent{get;init;}}}
