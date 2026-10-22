// Only native host boundaries are substituted. Tests execute the shipped DialogueApi source.
namespace EZapi {
 internal static class NativeItemLocalization {internal static string Locale="zh";internal static int Reads;internal static string CurrentLocaleCode{get{Reads++;return Locale;}}}
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession {internal static bool OnThread=true;internal static GameRunIdentity? Identity=new(1,"run",1);internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException();}internal static bool TryResolve(long? epoch,out Il2Cpp.PlayerStore player){player=new();return OnThread&&Identity is not null&&(!epoch.HasValue||epoch==Identity.Epoch);}}
 internal static class InventoryApi {internal static bool IsItemDragActive;}
 public sealed record NativeHookStatus(string OwnerId,bool Ready,IReadOnlyList<string> Targets,string? Error);
 internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null,string? Finalizer=null);
 internal sealed class NativeHookGroup {private readonly string _id;internal bool Ready;private string[] _targets=Array.Empty<string>();internal NativeHookGroup(string id)=>_id=id;internal void Install(Type callbacks,params NativeHookSpec[] specs){_targets=specs.Select(s=>s.Name).ToArray();Ready=true;}internal NativeHookStatus Snapshot()=>new(_id,Ready,_targets,null);internal void Stop()=>Ready=false;}
}
namespace MelonLoader {internal static class MelonLogger {internal static readonly List<string> Warnings=new();public static void Warning(string message)=>Warnings.Add(message);}}
namespace Il2CppSystem {public sealed class Action {private readonly System.Action _action;public Action(System.Action action)=>_action=action;public void Invoke()=>_action();}}
namespace Il2CppInterop.Runtime {internal static class DelegateSupport {public static T? ConvertDelegate<T>(System.Delegate action)where T:class=>new Il2CppSystem.Action((System.Action)action) as T;}}
namespace Il2Cpp {
 public class PlayerStore{public void LoadGame(){}public void StartNewGame(){}}
 public class MainMenuUIController{public void Awake(){}}
 public sealed class FakeTMP {public string text="";}
 public sealed class FakeGameObject {public bool activeInHierarchy;public void SetActive(bool active)=>activeInHierarchy=active;}
 public sealed class FakeDialogueChoiceBubble {public FakeGameObject gameObject=new(){activeInHierarchy=true};}
 public sealed class HandnoteUIManager {public static HandnoteUIManager? Instance;public FakeGameObject? panel;}
 public sealed class AdvCalendarUIManager {public static AdvCalendarUIManager? Instance;public FakeGameObject? UIPanel;}
 public sealed class TextBubble{public FakeTMP textfield=new();}
 public sealed class GameItem {
  private static int _next=1;public readonly IntPtr Pointer=(IntPtr)_next++;public object? parentInventory;public bool Released;public int DestroyCalls;public bool ThrowDestroy;
  public void Destroy(){DestroyCalls++;if(ThrowDestroy)throw new InvalidOperationException("intentional release failure");Released=true;}
 }
 public static class DirectoryMaster {
  public static readonly List<GameItem> Created=new();public static System.Action<GameItem>? OnCreate;
  public static GameItem Item(string id,bool fresh){var item=new GameItem();Created.Add(item);OnCreate?.Invoke(item);return item;}
 }
 public sealed class Dialogue {
  private static int _next=1000;public readonly IntPtr Pointer=(IntPtr)_next++;public string title="",plainText="";public int priority;public bool isMainDialog;public Dialogue? nextDialogue;public Il2CppSystem.Action? startAction,endAction;
  public readonly List<(GameItem Item1,Dialogue Item2,Il2CppSystem.Action? Item3)> choices=new();
  public Dialogue SetText(string speaker,string body){title=speaker;plainText=body;return this;}
  public Dialogue SetNextDialogue(Dialogue? next){nextDialogue=next;return this;}
  public bool IsChoiceEnabledAt(int index)=>index>=0&&index<choices.Count;
  public Dialogue AddChoice(GameItem item,string label,string description,Dialogue next,Il2CppSystem.Action? action,object? enabled){choices.Add((item,next,action));return this;}
 }
 public sealed class DialogUIManager {
  private static int _next=77;public static DialogUIManager? Instance=new();public IntPtr Pointer=(IntPtr)_next++;public Dialogue? currentDialog,displayingUncompletedDialog;public List<FakeDialogueChoiceBubble> dialogChoiceBubbles=new(){new(),new(),new()};public List<Dialogue> scheduledEndActionLines=new();public List<object> activeCoroutines=new();public bool isEndPending,isAutoAdvancePending,isSecMonologuePlaying;public int isConversationActiveFlag;public FakeTMP clientNameTMP=new();public FakeGameObject clientNameBubble=new();
  public System.Action? DuringStart;public bool ThrowStart;public bool RefuseInterrupt;
  public bool StartDialogue(Dialogue? dialogue,bool forcedStart=false){
   var before=new object?[]{this,dialogue,null};bool run=(bool)Invoke("BeforeStart",before)!;bool result=false;Exception? error=null;
   try{if(run){DuringStart?.Invoke();if(ThrowStart)throw new InvalidOperationException("intentional start failure");if(dialogue is not null&&(currentDialog is null||forcedStart||dialogue.priority>currentDialog.priority)){currentDialog=dialogue;result=true;DisplayClientText(dialogue);}}}
   catch(Exception e){error=e;}
   Invoke("AfterStart",new object?[]{this,result,error,before[2]});if(error is not null)throw error;return result;
  }
  public void DisplayClientText(Dialogue dialogue)=>Invoke("AfterClientText",new object?[]{this,dialogue,true});
  public void SelectChoice(int index){var prefix=new object?[]{this,index,true,null};Invoke("BeforeSelect",prefix);Exception? error=null;
   try{var source=currentDialog;if(source is not null&&source.IsChoiceEnabledAt(index)){source.choices[index].Item3?.Invoke();currentDialog=currentDialog!.choices[index].Item2;}}
   catch(Exception e){error=e;}finally{Invoke("AfterSelect",new object?[]{error,prefix[3]});}if(error is not null)throw error;
  }
  public void InterruptDialog(){if(RefuseInterrupt)return;currentDialog=null;displayingUncompletedDialog=null;isAutoAdvancePending=isEndPending=false;}
  internal static object? Invoke(string method,object?[] args){try{return typeof(EZapi.DialogueApi).GetMethod(method,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,args);}catch(System.Reflection.TargetInvocationException e){throw e.InnerException!;}}
 }
}
