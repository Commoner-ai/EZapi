using Il2Cpp;
namespace EZapi;

/// <summary>
/// 只保留身份与比较用指针值，不保留原生包装对象。场景切换立即失效，下一官方游戏事件再建立身份。
/// 同一槽/周目重新读档也会更换 Epoch，防止旧句柄把回滚前状态写回新载入对象。
/// </summary>
internal static class GameSession
{
    private static int _thread;
    private static long _epoch;
    private static IntPtr _pointer;
    private static GameRunIdentity? _identity;
    internal static GameRunIdentity? Identity=>_identity;
    internal static long Revision=>_epoch;
    internal static void Start() { _thread=Environment.CurrentManagedThreadId;Invalidate(); }
    internal static void Stop() { Invalidate();_thread=0; }
    internal static bool OnThread=>_thread!=0&&Environment.CurrentManagedThreadId==_thread;
    internal static void RequireThread()
    {
        if(!OnThread)throw new InvalidOperationException("EZAPI_RUN_THREAD: use the initialized game main thread.");
    }
    internal static void Invalidate() { _epoch++;_pointer=IntPtr.Zero;_identity=null; }
    internal static void Observe(GameEvent phase)
    {
        RequireThread();
        var player=PlayerStore.instance;
        if(!Valid(player)) { Invalidate();return; }
        if(phase==GameEvent.GameLoadedInit||_identity is null||!Matches(player!)) {
            _epoch++;
            _pointer=player!.Pointer;
            _identity=new(player.saveSlotId,player.runID,_epoch);
        }
    }
    // 新局（尤其跳过开场）不保证发出读档事件。宿主在场景 Start 完成且店铺库存可见后调用；
    // 不伪造 GameLoaded 事件，也不在任意 RunData 查询中自动复活已失效的句柄。
    internal static bool ObserveStoreReady()
    {
        RequireThread();
        if(!Valid(PlayerStore.instance))return false;
        if(_identity is null||!Matches(PlayerStore.instance!))Observe(GameEvent.GameLoadedNormal);
        return _identity is not null;
    }
    private static bool Valid(PlayerStore? player)=>player is not null&&player.Pointer!=IntPtr.Zero&&
        player.saveSlotId>=0&&!string.IsNullOrEmpty(player.runID);
    private static bool Matches(PlayerStore player)=>_identity is not null&&player.Pointer==_pointer&&
        player.saveSlotId==_identity.SaveSlotId&&player.runID==_identity.RunId;
    internal static bool TryResolve(long? epoch,out PlayerStore player)
    {
        player=null!;
        if(!OnThread||_identity is null||(epoch.HasValue&&epoch.Value!=_identity.Epoch))return false;
        var current=PlayerStore.instance;
        if(!Valid(current)||!Matches(current!)) { Invalidate();return false; }
        player=current!;return true;
    }
    internal static PlayerStore Resolve(GameRunIdentity identity)
    {
        RequireThread();
        if(!TryResolve(identity.Epoch,out var player))
            throw new InvalidOperationException("EZAPI_RUN_STALE: run handle expired; open a new handle in the current game event.");
        return player;
    }
}
