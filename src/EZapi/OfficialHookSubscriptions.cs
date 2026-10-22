using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>
/// 官方指南“订阅钩子”：启用时订阅、停用时取消同一委托。
/// 转换/异常隔离只属于 MelonLoader IL2CPP 宿主，不改变官方事件顺序或替换游戏方法。
/// </summary>
internal static class OfficialHookSubscriptions
{
    private static Il2CppSystem.Action<ModItemDirectory>? _directory;
    private static Il2CppSystem.Action<StoreClientManager>? _clientVisit;
    private static Il2CppSystem.Action<RichTextBuilder,GameItem>? _early, _late;
    internal static bool IsReady => _directory is not null && _early is not null && _late is not null;
    internal static bool ClientVisitReady
    {
        get
        {
            var expected=_clientVisit;
            if(expected is null)return false;
            try {
                var current=ModHook.OnGenerateCustomerVeryLate;
                if(current is null)return false;
                foreach(var handler in current.GetInvocationList())if(handler.Pointer==expected.Pointer)return true;
            }catch { }
            return false;
        }
    }
    internal static void Start()
    {
        if(_directory is null)
            try {
                _directory=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ModItemDirectory>>((Action<ModItemDirectory>)(d=>Guard("directory",()=>{InventoryRuntime.DirectoryReady();InventoryEndpointRuntime.DirectoryReady();ExternalPowerRuntime.DirectoryReady();LifecycleApi.DirectoryReady();ReadingApi.DirectoryReady();DialogueApi.DirectoryReady();PhoneApi.DirectoryReady();ModuleApi.DirectoryReady();ItemRegistry.Apply(d);DeclarativeItemLocalization.DirectoryReady();EffectApi.Apply();PresentationApi.DirectoryReady();ItemStockRuntime.DirectoryReady();NpcStockRuntime.DirectoryReady();})))
                    ??throw new InvalidOperationException("Unable to convert official directory callback.");
                _early=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<RichTextBuilder,GameItem>>((Action<RichTextBuilder,GameItem>)((b,i)=>Guard("tooltip early",()=>ItemPresentation.Early(b,i))))
                    ??throw new InvalidOperationException("Unable to convert official early callback.");
                _late=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<RichTextBuilder,GameItem>>((Action<RichTextBuilder,GameItem>)((b,i)=>Guard("tooltip late",()=>ItemPresentation.Late(b,i))))
                    ??throw new InvalidOperationException("Unable to convert official late callback.");
                ModHook.add_OnModItemDirectoryInit(_directory);
                ModHook.add_OnCreateTooltipEarly(_early);
                ModHook.add_OnCreateTooltipLate(_late);
            } catch { Stop(); throw; }

        // Customer support is an independent optional hook group. A missing event must not disable
        // the item directory or tooltip callbacks that already loaded successfully.
        if(_clientVisit is null)
            try {
                _clientVisit=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<StoreClientManager>>(
                    (Action<StoreClientManager>)(manager=>Guard("customer very late",()=>ClientVisitApi.OnGenerateCustomer(manager))))
                    ??throw new InvalidOperationException("Unable to convert official customer callback.");
                ModHook.add_OnGenerateCustomerVeryLate(_clientVisit);
            } catch(Exception e) {
                if(_clientVisit is not null)try{ModHook.remove_OnGenerateCustomerVeryLate(_clientVisit);}catch{}
                _clientVisit=null;
                MelonLogger.Warning("[EZapi] EZAPI_CLIENT_VISIT_HOOK_UNAVAILABLE: "+e.GetType().Name+": "+e.Message);
            }
    }
    internal static void Stop()
    {
        if(_clientVisit is not null)ModHook.remove_OnGenerateCustomerVeryLate(_clientVisit);
        if(_directory is not null)ModHook.remove_OnModItemDirectoryInit(_directory);
        if(_early is not null)ModHook.remove_OnCreateTooltipEarly(_early);
        if(_late is not null)ModHook.remove_OnCreateTooltipLate(_late);
        _directory=null; _clientVisit=null; _early=null; _late=null;
    }
    private static void Guard(string stage,Action action)
    {
        try { action(); }
        catch(Exception e) { MelonLogger.Error("[EZapi] EZAPI_HOOK_FAILED "+stage+": "+e); }
    }
}
