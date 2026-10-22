namespace EZapi;

/// <summary>
/// 自适应“离开店铺”地点按钮。初始化后主线程注册；框架在原生地图打开后创建并维护。
/// 不接管原版离店条件；没有空位则隐藏，地图/存档/场景改变即销毁自有控件。
/// </summary>
public static class DepartureApi
{
    internal static readonly DepartureCatalog Catalog = new();
    private static bool _started;
    internal static void Start() => _started = true;
    public static bool IsAvailable => _started && GameSession.OnThread && GameBuildFingerprint.IsKnown;

    /// <summary>最简入口；label为回退文字，onClick在当前地图和资格重新核对后同步调用。</summary>
    public static PresentationResult Register(string ownerId, string buttonId, string label, Action onClick)
    {
        if (onClick is null) return new(PresentationStatus.Invalid, "EZAPI_DEPARTURE_DEFINITION");
        return Register(ownerId, new DepartureButton(buttonId, label, _ => onClick()));
    }

    /// <summary>Queued仅表示声明接受；Snapshot中的Placed/Interactable才表示当前地图按钮已就位。</summary>
    public static PresentationResult Register(string ownerId, DepartureButton button)
    {
        if (!_started || !GameSession.OnThread) return new(PresentationStatus.Unavailable, "EZAPI_DEPARTURE_THREAD");
        var result = Catalog.Register(ownerId, button);
        if (result.Status == PresentationStatus.Queued) DepartureRuntime.InvalidateLayout();
        return result;
    }

    /// <summary>只撤销指定作者的入口及监听，不修改游戏按钮或其它Mod；可重复注销。</summary>
    public static PresentationResult Unregister(string ownerId, string buttonId)
    {
        if (!_started || !GameSession.OnThread) return new(PresentationStatus.Unavailable, "EZAPI_DEPARTURE_THREAD");
        var result = Catalog.Remove(ownerId, buttonId);
        if (result.Status == PresentationStatus.Closed) DepartureRuntime.Remove(buttonId);
        return result;
    }

    public static IReadOnlyList<DepartureButtonView> Snapshot()
    {
        GameSession.RequireThread();
        return Array.AsReadOnly(Catalog.Entries.Values.OrderBy(e => e.Definition.Order).ThenBy(e => e.Definition.Id, StringComparer.Ordinal).Select(e => e.View()).ToArray());
    }

    internal static void Stop()
    {
        _started = false;
        DepartureRuntime.SceneChanged();
        Catalog.Entries.Clear();
    }
}
