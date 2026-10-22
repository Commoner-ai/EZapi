using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>10个精确阶段。LoadGame/EndNight/EndDay与ModHook.Fire方法的时机见lifecycle-content.zh-CN.md。</summary>
public enum LifecyclePhase
{
    BeforeLoad,AfterLoad,BeforeNight,AfterNight,BeforeNightServices,AfterNightServices,
    AfterSleep,AfterEndDay,NightAttemptFinished,AfterGameLoadedLate
}
/// <summary>生命周期身份快照，允许BeforeLoad尚无周目ID；不能作为RunData句柄。</summary>
public sealed record LifecycleRunIdentity(string RunId,int SlotId,int Day);

/// <summary>只在当前同步回调链有效。不要保存Store/Items包装对象到下一帧或下一场景。</summary>
public sealed class LifecycleContext
{
    private readonly PlayerStore _store;
    private readonly IntPtr _pointer,_singleton;
    private readonly long _revision,_generation;
    private IReadOnlyList<GameItem>? _items;
    private bool _active=true;
    internal LifecycleContext(PlayerStore store,long generation)
    {
        _store=store;_pointer=store.Pointer;_singleton=PlayerStore.instance?.Pointer??IntPtr.Zero;
        _revision=GameSession.Revision;_generation=generation;
        int day;try{day=StoreStation.GetDayCounter();}catch{day=-1;}
        string id;int slot;
        try{id=store.runID??"";}catch{id="";}
        try{slot=store.saveSlotId;}catch{slot=-1;}
        Run=new(id,slot,day);
    }
    public LifecycleRunIdentity Run {get;}
    public bool IsCurrent {
        get {
            if(!_active||!GameSession.OnThread||!LifecycleApi.IsGeneration(_generation)||GameSession.Revision!=_revision)return false;
            try{return _store.Pointer==_pointer&&_pointer!=IntPtr.Zero&&SafeId(_store)==Run.RunId&&SafeSlot(_store)==Run.SlotId&&
                (PlayerStore.instance?.Pointer??IntPtr.Zero)==_singleton;}catch{return false;}
        }
    }
    public PlayerStore Store {get{RequireCurrent();return _store;}}
    /// <summary>每个订阅回调内惰性捕获的完整快照；后续订阅者重新捕获。修改库存后请显式重新扫描，不能继续使用已销毁物品的包装对象。</summary>
    public IReadOnlyList<GameItem> Items {get{RequireCurrent();Capture();return _items!;}}
    public bool ItemsComplete {get{RequireCurrent();Capture();return _itemsError is null;}}
    private string? _itemsError;
    public string? ItemsError {get{RequireCurrent();Capture();return _itemsError;}}
    private void Capture()
    {
        if(_items is not null)return;
        try{_items=Array.AsReadOnly(InventoryApi.CaptureScoped(_store,()=>IsCurrent).ToArray());}
        catch(Exception e){_items=Array.Empty<GameItem>();_itemsError=e.GetType().Name+": "+e.Message;}
    }
    // 早先订阅者可消费物品。保留本阶段身份门控，但不能把其旧包装对象交给后续订阅者。
    internal void ResetCapturedItems(){_items=null;_itemsError=null;}
    private void RequireCurrent(){if(!IsCurrent)throw new InvalidOperationException("EZAPI_LIFECYCLE_STALE: synchronous context expired.");}
    private static string SafeId(PlayerStore store){try{return store.runID??"";}catch{return "";}}
    private static int SafeSlot(PlayerStore store){try{return store.saveSlotId;}catch{return -1;}}
    internal void Close()=>_active=false;
}

public static class LifecycleApi
{
    private sealed class Subscription:IDisposable
    {
        internal readonly string Owner,Id;internal readonly LifecyclePhase Phase;internal readonly int Order;
        internal readonly Action<LifecycleContext> Callback;private int _disposed;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal Subscription(string owner,string id,LifecyclePhase phase,int order,Action<LifecycleContext> callback)=>(Owner,Id,Phase,Order,Callback)=(owner,id,phase,order,callback);
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Subscription> Subscriptions=new();
    private static readonly NativeHookGroup Hooks=new("ezapi.lifecycle");
    private static HashSet<LifecyclePhase> Active=new();
    private static bool _started;
    private static long _generation;
    internal static void Start(){if(_started)return;_started=true;_generation++;Active=new();}
    internal static bool IsGeneration(long generation)=>_started&&generation==_generation&&Hooks.Snapshot().Ready;
    public static IDisposable Subscribe(string ownerId,string callbackId,LifecyclePhase phase,Action<LifecycleContext> callback,int order=0)
    {
        GameSession.RequireThread();RunData.ValidateModId(ownerId);ValidateId(callbackId);ArgumentNullException.ThrowIfNull(callback);
        if(!Enum.IsDefined(typeof(LifecyclePhase),phase))throw new ArgumentOutOfRangeException(nameof(phase));
        if(!_started)throw new InvalidOperationException("EZAPI_LIFECYCLE_NOT_READY: initialize EZapi first.");
        Subscriptions.RemoveAll(s=>s.Disposed);
        if(Subscriptions.Any(s=>s.Owner==ownerId&&s.Id==callbackId))throw new ArgumentException("EZAPI_LIFECYCLE_DUPLICATE: owner/callbackId already registered.");
        if(Subscriptions.Count>=256)throw new InvalidOperationException("EZAPI_LIFECYCLE_LIMIT: max256 subscriptions.");
        var result=new Subscription(ownerId,callbackId,phase,order,callback);Subscriptions.Add(result);return result;
    }
    /// <summary>初始化时可先订阅；目录就绪后应检查Ready，缺少任一必需入口时整组停用。</summary>
    public static NativeHookStatus GetRuntimeStatus(){GameSession.RequireThread();return Hooks.Snapshot();}
    internal static void DirectoryReady()
    {
        if(!_started)return;
        GameSession.RequireThread();
        Hooks.Install(typeof(LifecycleApi),
            new(typeof(PlayerStore),nameof(PlayerStore.LoadGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad),Postfix:nameof(AfterLoad)),
            new(typeof(PlayerStore),nameof(PlayerStore.EndNight),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeNight),Postfix:nameof(AfterNight),Finalizer:nameof(NightException)),
            new(typeof(PlayerStore),nameof(PlayerStore.EndDay),typeof(void),Type.EmptyTypes,Postfix:nameof(AfterEndDay)),
            new(typeof(ModHook),nameof(ModHook.FireOnHandlingNightlyServicesEarly),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeServices)),
            new(typeof(ModHook),nameof(ModHook.FireOnHandlingNightlyServicesLate),typeof(void),Type.EmptyTypes,Postfix:nameof(AfterServices)),
            new(typeof(ModHook),nameof(ModHook.FireOnGoingSleepLate),typeof(void),Type.EmptyTypes,Postfix:nameof(AfterSleep)),
            new(typeof(ModHook),nameof(ModHook.FireOnGameLoadedLate),typeof(void),Type.EmptyTypes,Postfix:nameof(AfterLoadedLate)));
    }
    private static void BeforeLoad(PlayerStore __instance)=>Dispatch(LifecyclePhase.BeforeLoad,__instance);
    private static void AfterLoad(PlayerStore __instance,bool __runOriginal){if(__runOriginal)Dispatch(LifecyclePhase.AfterLoad,__instance);}
    private static void BeforeNight(PlayerStore __instance)=>Dispatch(LifecyclePhase.BeforeNight,__instance);
    private static void AfterNight(PlayerStore __instance,bool __runOriginal)
    {
        if(!_started||!GameSession.OnThread||!Hooks.Ready)return;
        var generation=_generation;var revision=GameSession.Revision;
        try {
            var pointer=__instance.Pointer;var runId=__instance.runID;var slot=__instance.saveSlotId;
            var singleton=PlayerStore.instance?.Pointer??IntPtr.Zero;
            if(__runOriginal)Dispatch(LifecyclePhase.AfterNight,__instance);
            if(generation==_generation&&revision==GameSession.Revision&&pointer==__instance.Pointer&&runId==__instance.runID&&
                slot==__instance.saveSlotId&&singleton==(PlayerStore.instance?.Pointer??IntPtr.Zero))Dispatch(LifecyclePhase.NightAttemptFinished,__instance);
        }catch(Exception e){Log(e);}
    }
    private static void BeforeServices()=>DispatchCurrent(LifecyclePhase.BeforeNightServices);
    private static void AfterServices(bool __runOriginal){if(__runOriginal)DispatchCurrent(LifecyclePhase.AfterNightServices);}
    private static void AfterSleep(bool __runOriginal){if(__runOriginal)DispatchCurrent(LifecyclePhase.AfterSleep);}
    private static void AfterEndDay(PlayerStore __instance,bool __runOriginal){if(__runOriginal)Dispatch(LifecyclePhase.AfterEndDay,__instance);}
    private static void AfterLoadedLate(bool __runOriginal){if(__runOriginal)DispatchCurrent(LifecyclePhase.AfterGameLoadedLate);}
    // finalizer只记录并原样返回异常；不把原生结算失败改成成功，不自动重试或保存。
    private static Exception? NightException(Exception? __exception)
    {
        if(__exception is not null&&_started&&Hooks.Ready)MelonLogger.Error("[EZapi] EZAPI_LIFECYCLE_NIGHT_EXCEPTION: "+__exception);
        return __exception;
    }
    private static void DispatchCurrent(LifecyclePhase phase)
    {if(!_started||!GameSession.OnThread||!Hooks.Ready)return;try{Dispatch(phase,PlayerStore.instance);}catch(Exception e){Log(e);}}
    private static void Dispatch(LifecyclePhase phase,PlayerStore? store)
    {
        if(!_started||!GameSession.OnThread||!Hooks.Snapshot().Ready||store is null)return;
        var active=Active;if(!active.Add(phase))return;
        LifecycleContext? context=null;
        try {
            if(store.Pointer==IntPtr.Zero)return;
            var subscribers=Subscriptions.Where(s=>!s.Disposed&&s.Phase==phase).OrderBy(s=>s.Order).ThenBy(s=>s.Owner,StringComparer.Ordinal).ThenBy(s=>s.Id,StringComparer.Ordinal).ToArray();
            if(subscribers.Length==0)return;
            context=new LifecycleContext(store,_generation);
            foreach(var s in subscribers) {
                if(!context.IsCurrent||!Hooks.Snapshot().Ready)break;
                if(s.Disposed)continue;
                context.ResetCapturedItems();
                try{s.Callback(context);}catch(Exception e){s.Dispose();MelonLogger.Error("[EZapi] EZAPI_LIFECYCLE_CALLBACK_FAILED "+s.Owner+"/"+s.Id+": "+e.Message);}
            }
        }catch(Exception e){Log(e);}finally{context?.Close();active.Remove(phase);Subscriptions.RemoveAll(s=>s.Disposed);}
    }
    internal static void Stop(){_started=false;_generation++;Hooks.Stop();foreach(var s in Subscriptions)s.Dispose();Subscriptions.Clear();Active=new();}
    internal static object Report()=>new{started=_started,hook=Hooks.Snapshot(),subscriptions=Subscriptions.Where(s=>!s.Disposed).Select(s=>new{s.Owner,s.Id,phase=s.Phase.ToString(),s.Order}).ToArray()};
    private static void Log(Exception e)=>MelonLogger.Error("[EZapi] EZAPI_LIFECYCLE_CAPTURE_FAILED: "+e.GetType().Name+": "+e.Message);
    private static void ValidateId(string value)
    {if(string.IsNullOrWhiteSpace(value)||value.Length>160||value.Any(char.IsControl))throw new ArgumentException("EZAPI_LIFECYCLE_CALLBACK_ID: readable ID <=160 required.");}
}
