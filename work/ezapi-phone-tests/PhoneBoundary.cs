namespace UnityEngine {public static class Time {public static double realtimeSinceStartupAsDouble;}}
namespace Il2CppSystem.Collections.Generic {
 public class List<T>:System.Collections.Generic.List<T>{}
 public class Dictionary<K,V>:System.Collections.Generic.Dictionary<K,V>where K:notnull{private static int _next=1;public readonly IntPtr Pointer=(IntPtr)_next++;}
}
namespace Il2Cpp {
 public class StorePhoneClient {
  private static int _next=1;public readonly IntPtr Pointer=(IntPtr)_next++;public enum PhoneClientType{None,Supplier}public enum PhoneState{None,NotResponding,Responding,Introduction,Regular,Cooldown}
  public PhoneClientType phoneClientType;public PhoneState phoneState;public string displayName="",locID="",dialogFuncId="";public bool dialedBefore;public int cooldownDuration,currentCooldown;public Il2CppSystem.Collections.Generic.List<string> alias=new();
  public string GetLocalizedDisplayName(){var args=new object?[]{this,null};return (bool)PhoneUIManager.Invoke("BeforeName",args)!?"Native":(string)args[1]!;}
  public bool ShownInPhoneBook(){var args=new object?[]{this,false};return (bool)PhoneUIManager.Invoke("BeforeShown",args)!?phoneState==PhoneState.Regular:(bool)args[1]!;}
  public Dialogue? GetCallDialog(){var args=new object?[]{this,null};return (bool)PhoneUIManager.Invoke("BeforeFactory",args)!?new Dialogue().SetText("Native","Foreign"):(Dialogue?)args[1];}
 }
 public class PhoneBookUIManager{public void OpenUI()=>PhoneUIManager.Invoke("BeforeOpen",Array.Empty<object?>());}
 public class PhoneUIManager {
  private static int _next=100;public readonly IntPtr Pointer=(IntPtr)_next++;public static PhoneUIManager? Instance=new();public FakeGameObject phonePanel=new(){activeInHierarchy=true};public StorePhoneClient? currentPhoneClient;public bool isTalking;public int NativeStarts;
  public void OpenUI(){Invoke("BeforeOpen",Array.Empty<object?>());phonePanel.SetActive(true);}
  public void StartPhoneDialog(long number){var args=new object?[]{this,number,false};bool run=(bool)Invoke("BeforeDial",args)!;Exception? error=null;
   try{if(run){NativeStarts++;var dict=EZapi.GameSession.Player.PhoneClientDict;if(dict.TryGetValue(number,out var client)){var graph=client.GetCallDialog();if(graph is not null){currentPhoneClient=client;isTalking=true;DialoguePhoneUIManager.Instance!.StartDialogue(graph);}}}}
   catch(Exception e){error=e;}finally{Invoke("AfterDial",new object?[]{error,args[2]});}if(error is not null)throw error;
  }
  public void OnPhoneDialogEnd(bool sound=true){Invoke("BeforeEnd",new object?[]{this});isTalking=false;currentPhoneClient=null;}
  public void CloseUI(){Invoke("BeforeEnd",new object?[]{this});phonePanel.SetActive(false);isTalking=false;currentPhoneClient=null;DialoguePhoneUIManager.Instance!.InterruptDialog();}
  internal static object? Invoke(string name,object?[] args){try{return typeof(EZapi.PhoneApi).GetMethod(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,args);}catch(System.Reflection.TargetInvocationException e){throw e.InnerException!;}}
 }
 public class DialoguePhoneUIManager {
  private static int _next=1;public IntPtr Pointer=(IntPtr)_next++;public static DialoguePhoneUIManager? Instance=new();public Dialogue? currentDialog,displayingUncompletedDialog;public System.Collections.Generic.List<object> dialogChoiceBubbles=new(){new(),new(),new()};public bool isAutoAdvancePending,isEndPending;public int isConversationActiveFlag;public FakeTMP clientNameTMP=new();public FakeGameObject clientNameBubble=new();public bool RefuseInterrupt;
  public void StartDialogue(Dialogue dialogue){if(!(bool)PhoneUIManager.Invoke("BeforeStart",new object?[]{this,dialogue})!)return;currentDialog=dialogue;DisplayClientText(dialogue);}
  public void DisplayClientText(Dialogue dialogue)=>PhoneUIManager.Invoke("AfterText",new object?[]{this,dialogue,true});
  public void SelectChoice(int index){var args=new object?[]{this,index,true,false};PhoneUIManager.Invoke("BeforeSelect",args);Exception? error=null;
   try{if(currentDialog is not null&&currentDialog.IsChoiceEnabledAt(index)){currentDialog.choices[index].Item3?.Invoke();currentDialog=currentDialog!.choices[index].Item2;}}
   catch(Exception e){error=e;}finally{PhoneUIManager.Invoke("AfterSelect",new object?[]{error,args[3]});}if(error is not null)throw error;
  }
  public void InterruptDialog(){if(RefuseInterrupt)return;currentDialog=displayingUncompletedDialog=null;isEndPending=isAutoAdvancePending=false;}
 }
}
