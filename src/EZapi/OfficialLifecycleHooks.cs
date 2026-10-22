using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>唯一生命周期原生订阅表；转换后的同一委托成对 add/remove，不使用反射或 Harmony。</summary>
internal static class OfficialLifecycleHooks
{
    private sealed record Hook(Il2CppSystem.Action Callback,Action<Il2CppSystem.Action> Remove);
    private static readonly List<Hook> Hooks=new();
    internal static void Start()
    {
        if(Hooks.Count!=0)throw new InvalidOperationException("EZAPI_EVENT_HOOKS: prior subscriptions still retained.");
        try {
            Add(GameEvent.GameLoadedInit,ModHook.add_OnGameLoadedInit,ModHook.remove_OnGameLoadedInit);
            Add(GameEvent.GameLoadedEarly,ModHook.add_OnGameLoadedEarly,ModHook.remove_OnGameLoadedEarly);
            Add(GameEvent.GameLoadedNormal,ModHook.add_OnGameLoadedNormal,ModHook.remove_OnGameLoadedNormal);
            Add(GameEvent.GameLoadedLate,ModHook.add_OnGameLoadedLate,ModHook.remove_OnGameLoadedLate);
            Add(GameEvent.ShutterOpenedEarly,ModHook.add_OnShutterOpenedEarly,ModHook.remove_OnShutterOpenedEarly);
            Add(GameEvent.ShutterOpenedLate,ModHook.add_OnShutterOpenedLate,ModHook.remove_OnShutterOpenedLate);
            Add(GameEvent.ShutterClosedEarly,ModHook.add_OnShutterClosedEarly,ModHook.remove_OnShutterClosedEarly);
            Add(GameEvent.ShutterClosedLate,ModHook.add_OnShutterClosedLate,ModHook.remove_OnShutterClosedLate);
            Add(GameEvent.GoingSleepEarly,ModHook.add_OnGoingSleepEarly,ModHook.remove_OnGoingSleepEarly);
            Add(GameEvent.GoingSleepLate,ModHook.add_OnGoingSleepLate,ModHook.remove_OnGoingSleepLate);
            Add(GameEvent.WakingUpEarly,ModHook.add_OnWakingUpEarly,ModHook.remove_OnWakingUpEarly);
            Add(GameEvent.WakingUpLate,ModHook.add_OnWakingUpLate,ModHook.remove_OnWakingUpLate);
            Add(GameEvent.LeavingStoreEarly,ModHook.add_OnLeavingStoreEarly,ModHook.remove_OnLeavingStoreEarly);
            Add(GameEvent.LeavingStoreLate,ModHook.add_OnLeavingStoreLate,ModHook.remove_OnLeavingStoreLate);
            Add(GameEvent.ReturningStoreEarly,ModHook.add_OnReturningStoreEarly,ModHook.remove_OnReturningStoreEarly);
            Add(GameEvent.ReturningStoreLate,ModHook.add_OnReturningStoreLate,ModHook.remove_OnReturningStoreLate);
            Add(GameEvent.HandlingNightlyServicesEarly,ModHook.add_OnHandlingNightlyServicesEarly,ModHook.remove_OnHandlingNightlyServicesEarly);
            Add(GameEvent.HandlingNightlyServicesLate,ModHook.add_OnHandlingNightlyServicesLate,ModHook.remove_OnHandlingNightlyServicesLate);
        } catch { Stop();throw; }
    }
    private static void Add(GameEvent phase,Action<Il2CppSystem.Action> add,Action<Il2CppSystem.Action> remove)
    {
        var callback=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(()=>GameEvents.Publish(phase)))
            ??throw new InvalidOperationException("EZAPI_EVENT_HOOKS: callback conversion failed: "+phase);
        // 先保留再 add；即使 add 部分成功后异常，也能用同一委托撤销。
        Hooks.Add(new(callback,remove));add(callback);
    }
    internal static void Stop()
    {
        foreach(var hook in Hooks.ToArray().Reverse()) {
            try { hook.Remove(hook.Callback);Hooks.Remove(hook); }
            catch(Exception e) {
                // 保留失败项强引用。Publish 已被门控停用，下次 Stop 可继续清理，绝不重复 add。
                MelonLogger.Error("[EZapi] EZAPI_EVENT_UNSUBSCRIBE_FAILED: "+e);
            }
        }
    }
}
