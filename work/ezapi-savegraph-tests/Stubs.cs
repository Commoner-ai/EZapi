using System.Text.Json;
namespace Il2CppSystem {
 public class Object {static long next=1;public IntPtr Pointer {get;set;}=new(Interlocked.Increment(ref next));public T Cast<T>() where T:class=>this as T??throw new InvalidCastException();public T? TryCast<T>() where T:class=>this as T;}
 public class Type:System.Object{}
}
namespace Il2CppSystem.Collections.Generic {
 public class List<T>:System.Collections.Generic.List<T>{public List(){}public List(IEnumerable<T> values):base(values){}}
 public class Dictionary<K,V>:System.Collections.Generic.Dictionary<K,V> where K:notnull{}
}
namespace Il2CppInterop.Runtime { public static class Il2CppType{public static Il2CppSystem.Type Of<T>()=>new();}}
namespace Il2Cpp {
 public class GraphNodeStorage:Il2CppSystem.Object {public Il2CppSystem.Collections.Generic.List<GraphNodeStorage> children=new();}
 public class GameInventory:GraphNodeStorage {public GameItem? Owner; public GameItem? GetParentItem()=>Owner;}
 public class GameItem:GraphNodeStorage {
  public int uniqueId;public string identifier="test.item";public int unitCount=1;public GameInventory? parentInventory;public Action<GameItem>? onLoaded;public bool Destroyed;
  public void Add(GameItem child){var inv=new GameInventory{Owner=this};children.Add(inv);inv.children.Add(child);child.parentInventory=inv;}
  public void Destroy(){Destroyed=true;foreach(var c in children)foreach(var i in c.children)if(i is GameItem gi)gi.Destroy();Pointer=IntPtr.Zero;}
 }
 public class PlayerStore:Il2CppSystem.Object{public static PlayerStore instance=new();public int currentUniqueId;}
 public static class DirectoryMaster { public static HashSet<string> Missing=new();public static bool Has<T>(string id)=>!Missing.Contains(id);}
 public class SaveState:Il2CppSystem.Object{public Il2CppSystem.Collections.Generic.List<SaveItemNode> saveItems=new();}
 public class GridShapeBuilder {public int width=1,height=1,minX,minY,orientation;public byte outside;public byte[]? data=new byte[]{1};}
 public class SaveItemNode:Il2CppSystem.Object {public string identifier="test.item";public int uniqueId,unitCount=1;public long uuid;public GridShapeBuilder? itemShape,itemModifiedShape;public Il2CppSystem.Collections.Generic.List<long> childItems=new();public Il2CppSystem.Collections.Generic.List<int> childItemInventoryNode=new();public GameItem? tempLink;}
 public static class SaveManager {
  public static long currentUUID;public static Il2CppSystem.Collections.Generic.Dictionary<GameItem,long>? itemHeap=new();public static Il2CppSystem.Collections.Generic.Dictionary<long,GameItem>? itemDict=new();public static string? saveJson="original";
  public static int EncodeCalls,DecodeCalls;public static Func<GraphNodeStorage,Il2CppSystem.Collections.Generic.List<SaveItemNode>>? EncodeOverride;public static Func<Il2CppSystem.Collections.Generic.List<SaveItemNode>,GameItem>? DecodeOverride;public static Action<GameItem>? Callback;public static bool ThrowDecode;public static GameItem? LastCreated;
  public static Il2CppSystem.Collections.Generic.List<SaveItemNode> EncodeNodes(GraphNodeStorage root){EncodeCalls++;if(EncodeOverride!=null)return EncodeOverride(root);var items=new System.Collections.Generic.List<GameItem>();var q=new Queue<GraphNodeStorage>();q.Enqueue(root);var seen=new HashSet<IntPtr>();while(q.Count>0){var n=q.Dequeue();if(!seen.Add(n.Pointer))continue;if(n is GameItem i)items.Add(i);foreach(var c in n.children)q.Enqueue(c);}var uuids=items.Select((x,i)=>(x,id:(long)i+1)).ToDictionary(x=>x.x,x=>x.id);var result=new Il2CppSystem.Collections.Generic.List<SaveItemNode>();foreach(var item in items){var node=new SaveItemNode{identifier=item.identifier,uniqueId=item.uniqueId,unitCount=item.unitCount,uuid=uuids[item]};var children=new System.Collections.Generic.List<GameItem>();foreach(var branch in item.children){if(branch is GameInventory inv)foreach(var c in inv.children)if(c is GameItem gi)children.Add(gi);}foreach(var c in children){node.childItems.Add(uuids[c]);node.childItemInventoryNode.Add(0);}result.Add(node);}saveJson="encoding scratch";currentUUID=999;return result;}
  public static GameItem DecodeNodes(Il2CppSystem.Collections.Generic.List<SaveItemNode> nodes){DecodeCalls++;if(DecodeOverride!=null)return DecodeOverride(nodes);var map=nodes.ToDictionary(n=>n.uuid,n=>n.tempLink=new GameItem{identifier=n.identifier,uniqueId=n.uniqueId,unitCount=n.unitCount,onLoaded=Callback});LastCreated=map.Values.First();if(ThrowDecode)throw new Exception("decode failed after create");foreach(var n in nodes)foreach(var id in n.childItems)map[n.uuid]!.Add(map[id]!);return map.Values.Single(i=>i!.parentInventory==null)!;}
 }
 public static class LoaderTrace{public static readonly System.Collections.Generic.List<string> Calls=new();public static void Add(string name,GameItem item)=>Calls.Add(name+":"+item.uniqueId);}
 public static class MachineHelper{public static void LoadUpgrade(GameItem i)=>LoaderTrace.Add("upgrade",i);public static void OnLoad(GameItem i)=>LoaderTrace.Add("machine",i);}
 public static class SpriteHelper{public static void UpdateWaterContainerSprite(GameItem i)=>LoaderTrace.Add("sprite",i);}
 public static class GeneralHelper{public static void LoadLockItem(GameItem i)=>LoaderTrace.Add("lock",i);public static void LoadNonOwnedToolTag(GameItem i)=>LoaderTrace.Add("tool",i);public static void LoadHazardousWasteTag(GameItem i)=>LoaderTrace.Add("waste",i);}
 public static class PlantTraitList{public static void LoadEdibleProduce(GameItem i)=>LoaderTrace.Add("produce",i);}
 public static class CurrentConverterHelper{public static void OnLoad(GameItem i)=>LoaderTrace.Add("converter",i);}
 public static class LockHelper{public static void LoadLockedContainer(GameItem i)=>LoaderTrace.Add("container",i);}
 public static class MachineBrokenHelper{public static void LoadBrokenMachine(GameItem i)=>LoaderTrace.Add("broken",i);}
 public static class MachineTurboBoosterAdv{public static void LoadTurboBoostEligibleTag(GameItem i)=>LoaderTrace.Add("turbo",i);}
}
namespace UnityEngine {public static class JsonUtility {
 public static int ParseCalls;
 public static string ToJson(Il2CppSystem.Object obj){var s=(Il2Cpp.SaveState)obj;return JsonSerializer.Serialize(new{saveItems=s.saveItems.Select(n=>new{n.identifier,n.uniqueId,n.unitCount,n.uuid,childItems=n.childItems.ToArray(),childItemInventoryNode=n.childItemInventoryNode.ToArray()})});}
 public static Il2CppSystem.Object FromJson(string json,Il2CppSystem.Type t){ParseCalls++;using var d=JsonDocument.Parse(json);var state=new Il2Cpp.SaveState();foreach(var e in d.RootElement.GetProperty("saveItems").EnumerateArray()){var n=new Il2Cpp.SaveItemNode{identifier=e.GetProperty("identifier").GetString()!,uniqueId=e.GetProperty("uniqueId").GetInt32(),unitCount=e.GetProperty("unitCount").GetInt32(),uuid=e.GetProperty("uuid").GetInt64()};foreach(var x in e.GetProperty("childItems").EnumerateArray())n.childItems.Add(x.GetInt64());foreach(var x in e.GetProperty("childItemInventoryNode").EnumerateArray())n.childItemInventoryNode.Add(x.GetInt32());state.saveItems.Add(n);}return state;}
}}
namespace EZapi {
 internal static class GameBuildFingerprint{internal static bool IsKnown=true;}
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession{static readonly int thread=Environment.CurrentManagedThreadId;internal static GameRunIdentity? Identity=new(1,"test",1);internal static bool OnThread=>Environment.CurrentManagedThreadId==thread;internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}internal static bool TryResolve(long? epoch,out Il2Cpp.PlayerStore store){store=Il2Cpp.PlayerStore.instance;return OnThread&&Identity!=null&&(!epoch.HasValue||epoch==Identity.Epoch);}internal static Il2Cpp.PlayerStore Resolve(GameRunIdentity r)=>TryResolve(r.Epoch,out var s)?s:throw new InvalidOperationException("stale");}
 public static class InventoryApi {public static readonly System.Collections.Generic.List<Il2Cpp.GameItem> Live=new();public static IReadOnlyList<Il2Cpp.GameItem> CaptureRunItems(Il2Cpp.PlayerStore store)=>Live.ToArray();}
}
