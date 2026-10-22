using Il2Cpp;
namespace EZapi;

/// <summary>运行时原生调用的共同边界。只允许当前已载入周目的初始化主线程；不缓存原生对象。</summary>
internal static class NativeAccess
{
    internal static GameRunIdentity RequireRun()
    {
        GameSession.RequireThread();
        if (!GameSession.TryResolve(null, out _))
            throw new InvalidOperationException("EZAPI_NATIVE_NOT_READY: use a current loaded-run callback; do not cache scene objects.");
        return GameSession.Identity!;
    }
    internal static GameItem RequireItem(GameItem? item)
    {
        RequireRun();
        if (item is null || item.Pointer == IntPtr.Zero || item.unitCount <= 0)
            throw new ArgumentException("EZAPI_NATIVE_ITEM: current live item required.");
        return item;
    }
    internal static bool SameRun(GameRunIdentity identity)
        => GameSession.TryResolve(identity.Epoch, out _) && GameSession.Identity == identity;
}
