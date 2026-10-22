using Il2Cpp;
namespace EZapi;

/// <summary>050REV5-L日历Update仅拼接显示文字，却同步取语言表；切换中延后此显示刷新，避免WaitForCompletion破坏异步组。</summary>
internal static class NativeLocaleCalendarGuard
{
    private static readonly NativeHookGroup Hooks=new("ezapi.guide.locale-calendar");
    private static long _deferred;
    internal static bool Start()
    {
        if(!GameBuildFingerprint.IsKnown)return false;
        Hooks.Install(typeof(NativeLocaleCalendarGuard),new NativeHookSpec(typeof(StoreCalendar),nameof(StoreCalendar.Update),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeUpdate)));
        return Hooks.Snapshot().Ready;
    }
    private static bool BeforeUpdate()
    {
        if(!Hooks.Ready||NativeGuideLocale.CanUpdateCalendar)return true;
        _deferred++;return false;
    }
    internal static void Stop(){Hooks.Stop();_deferred=0;}
    internal static object Report()=>new{hook=Hooks.Snapshot(),deferredFrames=_deferred,canUpdate=NativeGuideLocale.CanUpdateCalendar,scope="only native calendar text Update while locale completion event pending; no game-day changes"};
}
