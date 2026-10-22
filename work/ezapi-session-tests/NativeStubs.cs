// Policy tests only; native allocations, save files and Unity are not simulated.
namespace Il2CppSystem {
 public class Action { public System.Action Value=null!; public void Invoke()=>Value(); }
}
namespace Il2CppSystem.Collections.Generic {
 public class Dictionary<K,V>:System.Collections.Generic.Dictionary<K,V> where K:notnull { }
}
namespace Il2CppInterop.Runtime {
 public static class DelegateSupport {
  public static T ConvertDelegate<T>(Delegate d) where T:new() {
   var value=new T();((Il2CppSystem.Action)(object)value!).Value=(System.Action)d;return value;
  }
 }
}
namespace Il2Cpp {
 public class PlayerStore {
  static int next=100;public IntPtr Pointer=new(++next);public static PlayerStore? instance;
  public int saveSlotId=1;public string runID="run1";
  public Il2CppSystem.Collections.Generic.Dictionary<string,string>? modData;
 }
 public static class ModHook {
  public static readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<Il2CppSystem.Action>> Handlers=new();
  public static string? ThrowAddAt,ThrowRemoveAt;
  public static void Add(string phase,Il2CppSystem.Action callback) {
   if(!Handlers.TryGetValue(phase,out var list))Handlers[phase]=list=new();list.Add(callback);
   if(ThrowAddAt==phase)throw new Exception("partial add failure");
  }
  public static void Remove(string phase,Il2CppSystem.Action callback) {
   if(ThrowRemoveAt==phase)throw new Exception("remove failure");
   if(Handlers.TryGetValue(phase,out var list))list.Remove(callback);
  }
  public static void Fire(string phase) {
   if(Handlers.TryGetValue(phase,out var list))foreach(var callback in list.ToArray())callback.Invoke();
  }
  public static void add_OnGameLoadedInit(Il2CppSystem.Action c)=>Add("GameLoadedInit",c);
  public static void remove_OnGameLoadedInit(Il2CppSystem.Action c)=>Remove("GameLoadedInit",c);
  public static void add_OnGameLoadedEarly(Il2CppSystem.Action c)=>Add("GameLoadedEarly",c);
  public static void remove_OnGameLoadedEarly(Il2CppSystem.Action c)=>Remove("GameLoadedEarly",c);
  public static void add_OnGameLoadedNormal(Il2CppSystem.Action c)=>Add("GameLoadedNormal",c);
  public static void remove_OnGameLoadedNormal(Il2CppSystem.Action c)=>Remove("GameLoadedNormal",c);
  public static void add_OnGameLoadedLate(Il2CppSystem.Action c)=>Add("GameLoadedLate",c);
  public static void remove_OnGameLoadedLate(Il2CppSystem.Action c)=>Remove("GameLoadedLate",c);
  public static void add_OnShutterOpenedEarly(Il2CppSystem.Action c)=>Add("ShutterOpenedEarly",c);
  public static void remove_OnShutterOpenedEarly(Il2CppSystem.Action c)=>Remove("ShutterOpenedEarly",c);
  public static void add_OnShutterOpenedLate(Il2CppSystem.Action c)=>Add("ShutterOpenedLate",c);
  public static void remove_OnShutterOpenedLate(Il2CppSystem.Action c)=>Remove("ShutterOpenedLate",c);
  public static void add_OnShutterClosedEarly(Il2CppSystem.Action c)=>Add("ShutterClosedEarly",c);
  public static void remove_OnShutterClosedEarly(Il2CppSystem.Action c)=>Remove("ShutterClosedEarly",c);
  public static void add_OnShutterClosedLate(Il2CppSystem.Action c)=>Add("ShutterClosedLate",c);
  public static void remove_OnShutterClosedLate(Il2CppSystem.Action c)=>Remove("ShutterClosedLate",c);
  public static void add_OnGoingSleepEarly(Il2CppSystem.Action c)=>Add("GoingSleepEarly",c);
  public static void remove_OnGoingSleepEarly(Il2CppSystem.Action c)=>Remove("GoingSleepEarly",c);
  public static void add_OnGoingSleepLate(Il2CppSystem.Action c)=>Add("GoingSleepLate",c);
  public static void remove_OnGoingSleepLate(Il2CppSystem.Action c)=>Remove("GoingSleepLate",c);
  public static void add_OnWakingUpEarly(Il2CppSystem.Action c)=>Add("WakingUpEarly",c);
  public static void remove_OnWakingUpEarly(Il2CppSystem.Action c)=>Remove("WakingUpEarly",c);
  public static void add_OnWakingUpLate(Il2CppSystem.Action c)=>Add("WakingUpLate",c);
  public static void remove_OnWakingUpLate(Il2CppSystem.Action c)=>Remove("WakingUpLate",c);
  public static void add_OnLeavingStoreEarly(Il2CppSystem.Action c)=>Add("LeavingStoreEarly",c);
  public static void remove_OnLeavingStoreEarly(Il2CppSystem.Action c)=>Remove("LeavingStoreEarly",c);
  public static void add_OnLeavingStoreLate(Il2CppSystem.Action c)=>Add("LeavingStoreLate",c);
  public static void remove_OnLeavingStoreLate(Il2CppSystem.Action c)=>Remove("LeavingStoreLate",c);
  public static void add_OnReturningStoreEarly(Il2CppSystem.Action c)=>Add("ReturningStoreEarly",c);
  public static void remove_OnReturningStoreEarly(Il2CppSystem.Action c)=>Remove("ReturningStoreEarly",c);
  public static void add_OnReturningStoreLate(Il2CppSystem.Action c)=>Add("ReturningStoreLate",c);
  public static void remove_OnReturningStoreLate(Il2CppSystem.Action c)=>Remove("ReturningStoreLate",c);
  public static void add_OnHandlingNightlyServicesEarly(Il2CppSystem.Action c)=>Add("HandlingNightlyServicesEarly",c);
  public static void remove_OnHandlingNightlyServicesEarly(Il2CppSystem.Action c)=>Remove("HandlingNightlyServicesEarly",c);
  public static void add_OnHandlingNightlyServicesLate(Il2CppSystem.Action c)=>Add("HandlingNightlyServicesLate",c);
  public static void remove_OnHandlingNightlyServicesLate(Il2CppSystem.Action c)=>Remove("HandlingNightlyServicesLate",c);
 }
}
namespace MelonLoader {
 public static class MelonLogger {
  public static readonly System.Collections.Generic.List<string> Errors=new();
  public static void Error(string error)=>Errors.Add(error);
 }
}
