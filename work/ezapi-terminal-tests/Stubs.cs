// 明确的离线边界：这里只验证真实注册协调器/DTO/图鉴目录；工厂、准入与加工替身不代表IL2CPP生效。
namespace Il2Cpp {
 public sealed class GameItem {public IntPtr Pointer=new(1);public string identifier="";public object? parentInventory,contentWindow;public int unitCount=1;public object? mayStackItemFunc;public bool Destroyed;public string identifierName="";public void SetName(string s){}public void SetShape(object o){}public void Destroy()=>Destroyed=true;public bool IsTag(string s)=>false;}
 public sealed class GridShapeBuilder {public GridShapeBuilder(int w,int h){}public GridShapeBuilder SetDataFill(int i)=>this;public object Build()=>new();}
 public sealed class GameGridInventory{}
 public static class ItemDirectory {public static GameItem CreateEmptyItem(string id)=>new(){identifier=id};}
}
namespace EZapi {
 public sealed record ContentSubmissionResult(bool Accepted,string? Reason);
 public enum NativeApplicationStatus {Staged,Applied,Conflict,Failed}
 public enum NativeItemKind {Item}
 public sealed record NativeItemOptions(string? Name=null,string? ShortDescription=null,string? FlavorText=null,string? SourceName=null,bool ShowSource=true,LocalizedItemText? LocalizedName=null);
 public sealed record NativeModuleRegistration(string ItemId,Func<Il2Cpp.GameItem> Factory,NativeItemOptions? Options=null);
 public sealed record NativeApplicationView(string OwnerId,string ContentId,string Kind,NativeApplicationStatus Status,string? Reason);
 public sealed record NativeItemFactoryView(string ItemId,NativeApplicationStatus Status,long Created=0,long Failures=0,string? LastError=null);
 internal static class GameSession {static readonly int Thread=Environment.CurrentManagedThreadId;internal static void RequireThread(){if(Thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("wrong thread");}}
 internal static class NativeDelegateBridge {internal static object Stack()=>new();}
 public sealed record MachineInventory;
 public sealed record MachineBatchContext;
 public sealed record MachineLiquidPart;
 public sealed record MachineLiquidSnapshot;
 public sealed record InventoryPlacement;
 public sealed record MachineTransactionResult {public bool Committed=>false;}
 public interface IMachineBatchTransaction{}
 public static class ItemRegistry {public static string? Error;public static string? DeclarationRegistrationError()=>Error;public static bool ContainsDeclaration(string id)=>NativeFactoryRegistry.Items.ContainsKey(id);}
 internal static class NativeFactoryRegistry {
  internal static Dictionary<string,NativeModuleRegistration> Items=new();internal static bool Reject;
  internal static ContentSubmissionResult Submit(string owner,NativeItemKind kind,IReadOnlyList<NativeModuleRegistration> defs,bool publish=true){if(Reject)return new(false,"fixture rejected factory");foreach(var d in defs)Items.Add(d.ItemId,d);if(publish)ContentApi.PublishBatch(defs.Select(d=>new NativeApplicationView(owner,d.ItemId,"item",NativeApplicationStatus.Staged,null)));return new(true,null);}
 }
 public static class ContentApi {public static Action<NativeApplicationView>? Observer;internal static void PublishBatch(IEnumerable<NativeApplicationView> views){foreach(var v in views)Observer?.Invoke(v);}}
 public static class ItemApi {public static IReadOnlyList<NativeItemFactoryView> FactorySnapshot()=>NativeFactoryRegistry.Items.Keys.Select(id=>new NativeItemFactoryView(id,NativeApplicationStatus.Staged)).ToArray();}
 public static class MachineLayoutApi {public static MachineInventory Create(Il2Cpp.GameItem item,MachineLayoutDefinition layout){item.contentWindow=new();return new();}}
 public static class InventoryAdmissionApi {
  public static bool IsAvailable=>true;public static int Active;public static bool Reject;public static readonly List<Func<Il2Cpp.GameItem,Il2Cpp.GameGridInventory,bool>> Predicates=new();
  public static IDisposable Register(string owner,string item,string inv,Func<Il2Cpp.GameItem,Il2Cpp.GameGridInventory,bool> allows){if(Reject)throw new InvalidOperationException("admission limit");Active++;Predicates.Add(allows);return new Lease();}
  sealed class Lease:IDisposable {bool disposed;public void Dispose(){if(!disposed){Active--;disposed=true;}}}
 }
 public static class MachineRecipeApi {
  public static bool CanEdit=true;public static string? Reject;public static readonly Dictionary<string,IReadOnlyList<RegisteredMachineRecipe>> Catalog=new();
  public static bool HasCatalog(string id)=>Catalog.ContainsKey(id);public static string? CheckManaged(string id,int n)=>Reject;
  public static int LastMarkup;public static MachinePowerRule? LastPower;public static int Commits;
  internal static void CommitManaged(string owner,string id,IReadOnlyList<RegisteredMachineRecipe> recipes,MachinePowerRule? power,int markup){Catalog[id]=recipes;LastMarkup=markup;LastPower=power;Commits++;}
  internal static void RemoveManaged(string id)=>Catalog.Remove(id);
 }
 internal static class RecipeGuideBrowser {internal static bool Visible;internal static void Close()=>Visible=false;internal static bool Open()=>Visible=true;}
 internal static class NativeGuideLocale {internal static void Stop(){}}
 public static class JsonItems {public static void ValidateObject(System.Text.Json.JsonElement e,Type t,string path){}}
}

namespace EZapi { public static class MachineRuntimeApi {public static bool RuntimeInstalled;} public static class InventoryFreezeApi {public static bool IsAvailable=true;} }

namespace EZapi { public static class MachineContainerConditionApi { public static int Active; public static bool Reject; public static MachineLayoutDefinition? Last; public static IDisposable Register(string owner,string id,MachineLayoutDefinition spec){if(Reject)throw new InvalidOperationException("condition fixture rejection");Last=JsonMachineLayout.Copy(spec);Active++;return new Lease();}sealed class Lease:IDisposable {bool disposed;public void Dispose(){if(!disposed){Active--;disposed=true;}}}} }
