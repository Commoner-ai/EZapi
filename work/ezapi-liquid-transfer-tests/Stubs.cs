// 只用于离线事务与回调注入；不会证明原生比例算法、过滤反应、价格、Unity或保存重载。
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
 public sealed class Il2CppStructArray<T> {private readonly T[] _data;public Il2CppStructArray(T[] data)=>_data=data.ToArray();public int Length=>_data.Length;public T this[int index]{get=>_data[index];set=>_data[index]=value;}}
}
namespace Il2CppSystem.Collections.Generic {
 public sealed class Dictionary<K,V>:System.Collections.Generic.Dictionary<K,V> where K:notnull {private static int serial;public IntPtr Pointer{get;set;}=new(Interlocked.Increment(ref serial));}
}
namespace Il2Cpp {
 public sealed class TagState {
  private static int serial;public IntPtr Pointer{get;set;}=new(Interlocked.Increment(ref serial));
  public int Int;public string valueString="";public bool Enabled;public static bool AliasTagClone;
  public bool valueEnabled=>Enabled;public int valueInt=>Int;public float valueFloat;public long valueLong;public double valueDouble;public bool valueBool;public string internalValueString="",identifier="",identifierName="";
  public TagState(string id,string name){}public TagState Enable(){Enabled=true;return this;}public TagState SetString(string s){valueString=s;return this;}
  public int GetInt()=>Int;public TagState Copy()=>new("",""){Int=Int,valueString=valueString,Enabled=Enabled,valueFloat=valueFloat,valueLong=valueLong,valueDouble=valueDouble,valueBool=valueBool,internalValueString=internalValueString,identifier=identifier,identifierName=identifierName};
  public TagState Clone()=>AliasTagClone?this:Copy();
 }
 public sealed class TagSystem {
  private static int serial;public IntPtr Pointer{get;}=new(Interlocked.Increment(ref serial));
  public Il2CppSystem.Collections.Generic.Dictionary<string,TagState> dict=new();
  public static bool AliasClone;
  public TagSystem Clone(){if(AliasClone)return this;var copy=new TagSystem();foreach(var p in dict)copy.dict[p.Key]=p.Value.Copy();return copy;}
 }
 public sealed class GameInventory {
  private static int serial;public IntPtr Pointer=new(Interlocked.Increment(ref serial));public bool RemoveLocked,InsertLocked;
  public bool IsRemoveLocked()=>RemoveLocked;public bool IsInsertLocked()=>InsertLocked;
 }
 public sealed class GameItem {
  private static int serial;public GameItem(){uniqueId=Interlocked.Increment(ref serial);Pointer=new(uniqueId);state.dict["capacity"]=new("",""){Int=10000};}
  public IntPtr Pointer{get;set;}public int uniqueId;public int unitCount=1;public string identifier="test.bottle";
  public string name="测试瓶";public long unitValue=10;public TagSystem state=new();public GameInventory? parentInventory=new();public bool Owned=true,Container=true;
  public Action? Sync;public int Syncs;public GameItem? Filter;
  public void SyncModifiedState(){Syncs++;Sync?.Invoke();}
  public TagState? GetTagReadonly(string id)=>state.dict.GetValueOrDefault(id);
  public bool IsTag(string id)=>id=="LIQUID_CONTAINER_TAG"&&Container;
 }
 public static class GeneralHelper {public static bool IsItemOwned(GameItem item)=>item.Owned;}
 public sealed class Liquid {
  public string identifier="water",currentPart="water_volume";
  public static List<Liquid>? Liquids=new(){new(),new(){identifier="salt",currentPart="salt_volume"}};
 }
 public static class WaterHelper {
  public static Action<GameItem,int>? RemoveHook;public static Action<GameItem,string,int>? AddHook;public static Action<GameItem,GameItem>? TransferHook;
  public static Func<int[],int,int[]>? SplitHook;
  public static int Removes,Adds,Transfers;
  public static void Reset(){RemoveHook=null;AddHook=null;TransferHook=null;SplitHook=null;Removes=Adds=Transfers=0;}
  public static int Amount(GameItem i,string tag)=>i.GetTagReadonly(tag)?.Int??0;
  public static void Set(GameItem i,string id,int amount){var tag=Liquid.Liquids!.Single(p=>p.identifier==id).currentPart;i.state.dict[tag]=new(tag,tag){Int=amount};}
  public static int GetTotalVolume(GameItem item)=>Liquid.Liquids!.Sum(p=>Amount(item,p.currentPart));
  public static int GetFreeCapacity(GameItem item)=>Amount(item,"capacity")-GetTotalVolume(item);
  public static int GetWaterPurity(GameItem item)=>GetTotalVolume(item)==0?0:Amount(item,"water_volume")*100/GetTotalVolume(item);
  public static GameItem? GetFilterItem(GameItem item)=>item.Filter;
  public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> SplitProportionally(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p,int count) {
   var values=Enumerable.Range(0,p.Length).Select(i=>p[i]).ToArray();if(SplitHook is {} hook)return new(hook(values,count));
   long total=values.Sum();var assigned=values.Select(v=>(int)((long)v*count/total)).ToArray();var remaining=count-assigned.Sum();
   foreach(var n in Enumerable.Range(0,p.Length).OrderByDescending(i=>(long)values[i]*count%total).ThenBy(i=>i).Take(remaining))assigned[n]++;
   return new(assigned);
  }
  public static void Remove(GameItem item,int count){Removes++;if(RemoveHook is {} hook){hook(item,count);return;}RemoveDefault(item,count);}
  public static void RemoveDefault(GameItem item,int count){var liquids=Liquid.Liquids!;var split=SplitProportionally(new(liquids.Select(p=>Amount(item,p.currentPart)).ToArray()),count);for(var i=0;i<liquids.Count;i++)Set(item,liquids[i].identifier,Amount(item,liquids[i].currentPart)-split[i]);item.unitValue--;}
  public static void AddLiquid(GameItem item,string id,int count){Adds++;if(AddHook is {} hook){hook(item,id,count);return;}var liquid=Liquid.Liquids!.Single(p=>p.identifier==id);Set(item,id,checked(Amount(item,liquid.currentPart)+count));}
  public static void TransferLiquid(GameItem a,GameItem b){Transfers++;if(TransferHook is {} hook){hook(a,b);return;}var before=Liquid.Liquids!.Select(p=>Amount(a,p.currentPart)).ToArray();RemoveDefault(a,Math.Min(GetTotalVolume(a),GetFreeCapacity(b)));for(var i=0;i<before.Length;i++)AddLiquid(b,Liquid.Liquids![i].identifier,before[i]-Amount(a,Liquid.Liquids[i].currentPart));}
 }
 public sealed class ItemCondition {public string identifier="water";}
 public static class WaterFeatureHelper {public static ItemCondition GetConditionFromPurity(int p)=>new();}
 public static class ItemConditionList {public static ItemCondition CreatePureWater()=>new(){identifier="pure"};public static ItemCondition CreateHighQualityWater()=>new(){identifier="high"};public static ItemCondition CreateBaseWater()=>new(){identifier="base"};}
}
namespace EZapi {
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession {
  internal static readonly PlayerStore Store=new();internal static PlayerStore Resolve(GameRunIdentity run){if(Identity!=run)throw new InvalidOperationException("stale");return Store;}
  internal static readonly int MainThread=Environment.CurrentManagedThreadId;internal static GameRunIdentity? Identity=new(1,"test",1);
  internal static void RequireThread(){if(Environment.CurrentManagedThreadId!=MainThread)throw new InvalidOperationException("thread");}
  internal static bool TryResolve(long? epoch,out object store){store=new();return Environment.CurrentManagedThreadId==MainThread&&Identity is {} run&&(!epoch.HasValue||run.Epoch==epoch);}
 }
 internal static class EZapiBootstrap {internal static bool WriteDiagnostics()=>true;}
 internal sealed class PlayerStore {internal List<Il2Cpp.GameItem> Items=new();internal List<Il2Cpp.GameItem> FindAllItem(bool all)=>Items;}
 internal static class InventoryApi {internal static bool IsItemDragActive;}
 internal static class InventoryFreezeApi {
  internal static bool IsAvailable=true;internal static Action? AcquireHook;internal static bool Refuse;
  private static readonly HashSet<IntPtr> Held=new();
  public static bool IsFrozen(Il2Cpp.GameItem item)=>IsAvailable&&Held.Contains(item.Pointer);
  public static void Reset(){Held.Clear();IsAvailable=true;Refuse=false;AcquireHook=null;}
  public static IDisposable? TryAcquire(IEnumerable<Il2Cpp.GameItem> items){if(!IsAvailable||Refuse)return null;var list=items.ToArray();if(list.Any(IsFrozen))return null;foreach(var i in list)Held.Add(i.Pointer);AcquireHook?.Invoke();return new Lease(list.Select(i=>i.Pointer).ToArray());}
  private sealed record Lease(IntPtr[] Pointers):IDisposable {public void Dispose(){foreach(var p in Pointers)Held.Remove(p);}}
 }
}


