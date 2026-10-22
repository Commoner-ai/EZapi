using MelonLoader;
namespace EZapi;

/// <summary>与官方 ModHook 一一对应；事件不是每日一次或存档落盘完成的保证。</summary>
public enum GameEvent
{
    GameLoadedInit,GameLoadedEarly,GameLoadedNormal,GameLoadedLate,
    ShutterOpenedEarly,ShutterOpenedLate,ShutterClosedEarly,ShutterClosedLate,
    GoingSleepEarly,GoingSleepLate,WakingUpEarly,WakingUpLate,
    LeavingStoreEarly,LeavingStoreLate,ReturningStoreEarly,ReturningStoreLate,
    HandlingNightlyServicesEarly,HandlingNightlyServicesLate
}

/// <summary>可保存到日志的纯托管事件快照，不含玩家、物品或库存引用。</summary>
public sealed record GameEventContext(GameEvent Event,long Sequence,GameRunIdentity? Run);

/// <summary>
/// 官方游戏事件的托管订阅入口。订阅在游戏主线程建立；返回句柄可 Dispose，不重复安装原生钩子。
/// 回调同步执行；异常只移除当前订阅。按同一事件内订阅顺序调用，不改变官方阶段顺序。
/// </summary>
public static class GameEvents
{
    internal static bool IsStarted=>_started;
    private sealed class Subscription:IDisposable
    {
        internal readonly string ModId;
        internal readonly GameEvent Event;
        internal readonly Action<GameEventContext> Callback;
        private int _disposed;
        internal bool Disposed=>System.Threading.Volatile.Read(ref _disposed)!=0;
        internal Subscription(string modId,GameEvent phase,Action<GameEventContext> callback)
            =>(ModId,Event,Callback)=(modId,phase,callback);
        public void Dispose()=>System.Threading.Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Subscription> Subscriptions=new();
    private static readonly Queue<GameEventContext> History=new();
    private static readonly Queue<string> Errors=new();
    private static readonly HashSet<GameEvent> Publishing=new();
    private static bool _started;
    private static long _sequence;
    private static long _generation;

    public static IDisposable Subscribe(string modId,GameEvent phase,Action<GameEventContext> callback)
    {
        GameSession.RequireThread();
        if(!_started)throw new InvalidOperationException("EZAPI_EVENT_NOT_READY: subscribe after EZapi initialization.");
        RunData.ValidateModId(modId);
        ArgumentNullException.ThrowIfNull(callback);
        if(!Enum.IsDefined(typeof(GameEvent),phase))throw new ArgumentOutOfRangeException(nameof(phase));
        Subscriptions.RemoveAll(s=>s.Disposed);
        if(Subscriptions.Count>=256)throw new InvalidOperationException("EZAPI_EVENT_LIMIT: max 256 active subscriptions.");
        var subscription=new Subscription(modId,phase,callback);
        Subscriptions.Add(subscription);return subscription;
    }
    public static IReadOnlyList<GameEventContext> GetRecentEvents()
    { GameSession.RequireThread();return History.ToArray(); }

    internal static void Start()
    {
        if(_started)return;
        GameSession.Start();
        try { OfficialLifecycleHooks.Start();_generation++;_started=true; }
        catch { Stop();throw; }
    }
    internal static void Stop()
    {
        _started=false;_generation++;
        foreach(var subscription in Subscriptions)subscription.Dispose();
        Subscriptions.Clear();History.Clear();Errors.Clear();
        GameSession.Stop();OfficialLifecycleHooks.Stop();
    }
    internal static void Publish(GameEvent phase)
    {
        if(!_started||!Publishing.Add(phase))return;
        var generation=_generation;
        try {
            GameSession.Observe(phase);
            var context=new GameEventContext(phase,++_sequence,GameSession.Identity);
            History.Enqueue(context);if(History.Count>64)History.Dequeue();
            foreach(var subscription in Subscriptions.Where(s=>s.Event==phase&&!s.Disposed).ToArray()) {
                if(!_started||generation!=_generation)break;
                // 前一个订阅若引发场景切换/读档，后续订阅不能再收到旧周目的有效数据上下文。
                if(context.Run is not null&&!GameSession.TryResolve(context.Run.Epoch,out _))break;
                if(subscription.Disposed)continue;
                try { subscription.Callback(context); }
                catch(Exception e) {
                    subscription.Dispose();
                    Record("EZAPI_EVENT_CALLBACK_FAILED "+subscription.ModId+" "+phase+": "+e);
                }
            }
            Subscriptions.RemoveAll(s=>s.Disposed);
        } catch(Exception e) { Record("EZAPI_EVENT_DISPATCH_FAILED "+phase+": "+e); }
        finally { Publishing.Remove(phase); }
    }
    private static void Record(string error)
    {
        Errors.Enqueue(error);if(Errors.Count>32)Errors.Dequeue();
        MelonLogger.Error("[EZapi] "+error);
    }
    internal static object Report()=>new {ready=_started,run=GameSession.Identity,
        subscriptions=Subscriptions.Where(s=>!s.Disposed).Select(s=>new{modId=s.ModId,phase=s.Event.ToString()}).ToArray(),
        recent=History.Select(c=>new{phase=c.Event.ToString(),c.Sequence,c.Run}).ToArray(),errors=Errors.ToArray()};
}
