using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Localization;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>只观察公开异步加载状态；预览工厂可能同步查本地化，故语言资源未就绪时延后，不WaitForCompletion。</summary>
internal static class NativeGuideLocale
{
    private static Il2CppSystem.Action<Locale>? _changed;
    private static string? _readyLocale;
    internal static bool CanUpdateCalendar=>_readyLocale is null||_readyLocale==NativeItemLocalization.CurrentLocaleCode;
    private static void Subscribe()
    {
        if(_changed is not null)return;
        var changed=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Locale>>((Action<Locale>)(locale=>{_readyLocale=locale.Identifier.Code;State="locale-event-ready";Error=null;}))
            ??throw new InvalidOperationException("locale event conversion failed");
        if(!NativeLocaleCalendarGuard.Start())throw new InvalidOperationException("locale calendar guard unavailable on current build");
        LocalizationSettings.add_SelectedLocaleChanged(changed);_changed=changed;
    }
    internal static void Stop()
    {
        try{NativeLocaleCalendarGuard.Stop();if(_changed is not null)LocalizationSettings.remove_SelectedLocaleChanged(_changed);}
        finally{_changed=null;_readyLocale=null;State="not-probed";Error=null;}
    }
    internal static string State{get;private set;}="not-probed";
    internal static string? Error{get;private set;}
    private static bool Pending(string state){State=state;return false;}
    internal static bool IsReady
    {
        get {
            try {
                Error=null;
                Subscribe();
                var code=NativeItemLocalization.CurrentLocaleCode;
                // Unity事件只在新语言初始化完成后发出。切换途中不读取InitializationOperation，避免递归触发初始化。
                if(_readyLocale is not null){if(_readyLocale!=code)return Pending("locale-event-pending");State="ready";return true;}
                var initialization=LocalizationSettings.InitializationOperation;
                if(!initialization.IsValid())return Pending("initialization-invalid");
                if(!initialization.IsDone)return Pending("initialization-pending");
                if(initialization.Status!=AsyncOperationStatus.Succeeded)return Pending("initialization-status-"+initialization.Status);
                var selected=LocalizationSettings.SelectedLocaleAsync;
                if(!selected.IsValid())return Pending("locale-invalid");
                if(!selected.IsDone)return Pending("locale-pending");
                if(selected.Status!=AsyncOperationStatus.Succeeded)return Pending("locale-status-"+selected.Status);
                var database=LocalizationSettings.StringDatabase;if(database is null)return Pending("database-unavailable");
                var preload=database.PreloadOperation;
                if(!preload.IsValid())return Pending("preload-invalid");
                if(!preload.IsDone)return Pending("preload-pending");
                if(preload.Status!=AsyncOperationStatus.Succeeded)return Pending("preload-status-"+preload.Status);
                _readyLocale=code;State="ready";return true;
            }catch(Exception error){Error=error.GetType().Name+": "+error.Message;return Pending("handle-unavailable");}
            // 语言切换会使旧句柄失效；诊断保留状态/异常，下帧重新获取，不持有句柄或缓存失败。
        }
    }
}
