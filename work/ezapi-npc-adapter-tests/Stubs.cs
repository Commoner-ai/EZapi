namespace Il2CppSystem {public sealed class Action {public System.Action Handler=()=>{};public void Invoke()=>Handler();}}
namespace Il2CppSystem.Collections.Generic {public class List<T>:System.Collections.Generic.List<T>{}}
namespace Il2Cpp {
 public sealed class GameInventory {}
 public sealed class GameItem {static int next;public IntPtr Pointer=new(++next);public string identifier="";public int uniqueId=++next;public int unitCount=1;public bool Owned,Destroyed;public GameInventory? parentInventory;public HashSet<string> Types=new();public IEnumerable<string> itemTypes=>Types;public bool IsGameItemType(string type)=>Types.Contains(type);public void Destroy(){Destroyed=true;}}
 public sealed class PlayerStore {
  public Il2CppSystem.Collections.Generic.List<GameItem> Items=new();
  public Il2CppSystem.Collections.Generic.List<GameItem> FindAllItem(bool all)=>Items;
  public void AddDirectSellingItemToTable(GameItem item,bool a=false,bool b=false,bool c=false,int d=0){}
 }
 public static class GeneralHelper {public static bool IsItemOwned(GameItem item)=>item.Owned;}
 public static class StoreStation {public static int Day=4;public static int GetDayCounter()=>Day;}
 public sealed class LootEntry {public string id,sourceModId;public float weight;public LootEntry(string id,float weight,string source){this.id=id;this.weight=weight;sourceModId=source;}}
 public sealed class LootRollContext {public string tableId="",groupId="",itemId="";}
 public static class LootRegistry {
  public static int Picks,OriginalRolls;public static bool Throw;public static Il2CppSystem.Collections.Generic.List<LootEntry>? Last;
  public static string Pick(Il2CppSystem.Collections.Generic.List<LootEntry> list){Picks++;Last=list;if(Throw)throw new Exception("native pick failure");return list.Last().id;}
  public static string Roll(string table)=>RollInternal(table,"");
  public static string RollInternal(string table,string group){OriginalRolls++;return "base";}
 }
 public static class ModHook {
  public static System.Action<LootRollContext>? Callback;public static int Events;
  public static void FireOnLootRolled(LootRollContext context){Events++;Callback?.Invoke(context);}
 }
 public static class DirectoryMaster {public static GameItem Item(string id,bool owned=true)=>new(){identifier=id,Owned=owned};}
 public static class ItemSpawner {public static GameItem Spawn(string id)=>new(){identifier=id};public static GameItem SpawnFromTable(string id)=>new(){identifier=id};public static GameItem SpawnFromTableGroup(string id)=>new(){identifier=id};}
 public sealed class StoreClient {public Il2CppSystem.Action? OnArrival;}
 public static class StoreClientList {
  public static void PlaceInventorInventory(bool visit=false){}
  public static StoreClient CreateMiner()=>new(){OnArrival=new(){Handler=()=>{}}};
  public sealed class __c {public static __c __9=new();public void _CreateThief_b__47_0(){}public void _CreatePettyThief_b__48_0(){}public void _CreateFoodThief_b__49_0(){}public void _CreateMiner_b__38_0(){}}
 }
}
namespace MelonLoader {public static class MelonLogger {public static void Warning(string text){}}}
namespace EZapi {
 public sealed record GameRunIdentity(int Slot,int Epoch=1);
 internal static class GameSession {
  internal static bool OnThread=true;internal static GameRunIdentity? Identity=new(533);internal static Il2Cpp.PlayerStore Player=new();
  internal static Il2Cpp.PlayerStore Resolve(GameRunIdentity run)=>Player;
 }
 internal static class NativeAccess {
  internal static GameRunIdentity RequireRun(){if(!GameSession.OnThread)throw new InvalidOperationException("thread");return GameSession.Identity??throw new InvalidOperationException("no run");}
  internal static bool SameRun(GameRunIdentity run)=>run==GameSession.Identity;
 }
 internal static class NativeFactoryRegistry {
  internal static readonly List<(string Owner,string Id,NpcTradeStockOptions Options)> Rows=new();
  internal static IEnumerable<(string Owner,string Id,NpcTradeStockOptions Options)> NpcStockDeclarations()=>Rows.ToArray();
  internal static bool Contains(string id)=>Rows.Any(r=>r.Id==id);
 }
 internal sealed record Definition(string Id,NpcTradeStockOptions? NpcTrade,string? ModId=null);
 internal static class ItemRegistry {
  internal static readonly List<Definition> Definitions=new();internal static readonly HashSet<string> NotApplied=new();
  internal static IEnumerable<Definition> AllDefinitions()=>Definitions.ToArray();
  internal static string GetStatus(string id)=>NotApplied.Contains(id)?"Failed":"Applied";
 }
 internal static class DebugNpcStock {internal static bool Running;}
 public sealed record LootEntrySnapshot(string Id,float Weight,string? SourceModId=null);
 public sealed record LootListSnapshot(bool Found,IReadOnlyList<LootEntrySnapshot> Entries);
 public static class LootTables {
  public static bool Found=true;public static LootEntrySnapshot[] Rows={new("iron",1)};
  public static LootListSnapshot GetSnapshot(string id)=>new(Found,Rows);
 }
 internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
 public sealed record NativeHookStatus(bool Ready);
 internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Finalizer=null,string? Postfix=null);
 internal sealed class NativeHookGroup {
  public static bool Live=true;private bool _ready;internal static NativeHookSpec[] Specs=Array.Empty<NativeHookSpec>();
  internal NativeHookGroup(string owner){}
  internal void Install(Type owner,params NativeHookSpec[] specs){Specs=specs;_ready=true;}
  internal NativeHookStatus Snapshot()=>new(_ready&&Live);
  internal void Stop()=>_ready=false;
 }
 public static class Items {internal static Il2Cpp.GameItem Create(string id,bool owned)=>Il2Cpp.DirectoryMaster.Item(id,owned);}
}
