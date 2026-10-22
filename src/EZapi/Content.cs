using MelonLoader;
namespace EZapi;

/// <summary>Staged只表示声明已提交；Applied才表示原生目录接受。不是存档提交结果。</summary>
public enum NativeApplicationStatus { Staged, Applied, Conflict, Failed }
public sealed record NativeApplicationView(string OwnerId,string ContentId,string Kind,NativeApplicationStatus Status,string? Reason);

/// <summary>全局内容应用通知。ownerId标识订阅者，不是内容过滤器；按view.OwnerId自行筛选。</summary>
public static class ContentApi
{
    private sealed class Subscription:IDisposable
    {
        internal readonly string Owner,Id;
        internal readonly Action<NativeApplicationView> Callback;
        private int _disposed;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal Subscription(string owner,string id,Action<NativeApplicationView> callback)=>(Owner,Id,Callback)=(owner,id,callback);
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Subscription> Subscriptions=new();
    private static readonly Queue<NativeApplicationView> Pending=new();
    private static bool _started,_dispatching;
    private static long _generation;
    public static IDisposable Subscribe(string ownerId,string callbackId,Action<NativeApplicationView> callback)
    {
        GameSession.RequireThread();RunData.ValidateModId(ownerId);ValidateId(callbackId);ArgumentNullException.ThrowIfNull(callback);
        if(!_started)throw new InvalidOperationException("EZAPI_CONTENT_NOT_READY: initialize EZapi first.");
        Subscriptions.RemoveAll(s=>s.Disposed);
        if(Subscriptions.Any(s=>s.Owner==ownerId&&s.Id==callbackId))throw new ArgumentException("EZAPI_CONTENT_DUPLICATE: owner/callbackId already registered.");
        if(Subscriptions.Count>=256)throw new InvalidOperationException("EZAPI_CONTENT_LIMIT: max256 subscriptions.");
        var result=new Subscription(ownerId,callbackId,callback);Subscriptions.Add(result);return result;
    }
    internal static void Start(){if(_started)return;_generation++;_started=true;}
    internal static void Stop()
    {
        _generation++;_started=false;Pending.Clear();
        foreach(var s in Subscriptions)s.Dispose();Subscriptions.Clear();
    }
    // 先加入整批通知再分发；订阅内注册另一批时只排队，避免递归和批次间交错。
    internal static void PublishBatch(IEnumerable<NativeApplicationView> views)
    {
        if(!_started)return;
        GameSession.RequireThread();
        foreach(var view in views) {
            if(Pending.Count>=8192){MelonLogger.Error("[EZapi] EZAPI_CONTENT_OVERFLOW: notification queue exceeded 8192; remaining notifications dropped.");break;}
            Pending.Enqueue(view);
        }
        if(_dispatching)return;
        _dispatching=true;var generation=_generation;var budget=8192;
        try {
            while(_started&&generation==_generation&&Pending.Count>0&&budget-->0) {
                var view=Pending.Dequeue();
                foreach(var s in Subscriptions.Where(s=>!s.Disposed).OrderBy(s=>s.Owner,StringComparer.Ordinal).ThenBy(s=>s.Id,StringComparer.Ordinal).ToArray()) {
                    if(!_started||generation!=_generation)break;
                    if(s.Disposed)continue;
                    try{s.Callback(view);}catch(Exception e){s.Dispose();MelonLogger.Error("[EZapi] EZAPI_CONTENT_CALLBACK_FAILED "+s.Owner+"/"+s.Id+": "+e.Message);}
                }
            }
            if(generation==_generation&&Pending.Count>0){Pending.Clear();MelonLogger.Error("[EZapi] EZAPI_CONTENT_OVERFLOW: reentrant notifications exceeded dispatch budget.");}
        } finally {
            if(generation!=_generation&&Pending.Count>0) {
                Pending.Clear();MelonLogger.Error("[EZapi] EZAPI_CONTENT_RESTART: notifications queued during a dispatch restart were discarded; register after shutdown completes.");
            }
            _dispatching=false;Subscriptions.RemoveAll(s=>s.Disposed);
        }
    }
    internal static object Report()=>new{ready=_started,generation=_generation,pending=Pending.Count,subscriptions=Subscriptions.Where(s=>!s.Disposed).Select(s=>new{s.Owner,s.Id}).ToArray()};
    private static void ValidateId(string value)
    {if(string.IsNullOrWhiteSpace(value)||value.Length>160||value.Any(char.IsControl))throw new ArgumentException("EZAPI_CONTENT_CALLBACK_ID: readable ID <=160 required.");}
}
