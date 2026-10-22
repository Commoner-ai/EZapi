using Il2Cpp;

namespace EZapi;

/// <summary>Native acceptance fixture for SupplierApi. Its callbacks only update counters; no money, inventory or save data is touched.</summary>
internal static class DebugSupplier
{
    private const string Owner = "ezapi.debug.supplier";
    private const string Actor = Owner + ".stockist";
    private const string DefinitionId = Owner + ".definition";
    private const string OfferId = Owner + ".sample_offer";
    private static long _sequence, _nextCheckAt;
    private static string? _request, _spriteName, _stamp;
    private static GameRunIdentity? _identity;
    private static int _captureCalls, _purchaseCalls, _dismissCalls;
    private static PresentationResult? _queueResult;

    internal static object? LastReport { get; private set; }

    internal static string Queue()
    {
        if (!GameSession.OnThread || !VisibleStoreInventory.IsReady || !GameSession.TryResolve(null, out _) || GameSession.Identity is not { } identity)
            return "请先进入有效测试周目的店铺并显示主库存。";

        if (_identity == identity && _request is not null)
        {
            var previous = SupplierApi.Status(Owner, _request);
            if (previous.Status is PresentationStatus.Queued or PresentationStatus.Arrived)
                return $"测试供应商仍处于 {previous.Status}；请先完成或离开当前访问。";
        }

        var spriteName = FindLoadedNativeSprite();
        if (spriteName is null)
            return "当前没有可复用的原生顾客肖像。请等一位原生顾客出现在店铺后再排入测试供应商。";

        _identity = identity;
        _request = Owner + ".request." + (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _spriteName = spriteName;
        _captureCalls = _purchaseCalls = _dismissCalls = 0;
        _queueResult = null;
        _nextCheckAt = 0;
        var definition = new SupplierDefinition(
            DefinitionId,
            "EZapi 测试供应商",
            Capture,
            _ => _dismissCalls++,
            ConfirmLabel: "确认测试报价",
            BackLabel: "返回测试报价",
            ContinueLabel: "刷新测试报价",
            LeaveLabel: "离开测试供应商",
            FailureText: "测试报价操作失败，请查看 EZapi 日志。")
        {
            RequireConfirmation = true,
            AllowMenuRefresh = true,
            CanContinue = _ => true
        };

        var queued = SupplierApi.Queue(Owner, _request, identity.RunId, identity.SaveSlotId,
            Actor, spriteName, definition);
        _queueResult = queued;
        WriteReport(SupplierApi.Status(Owner, _request));
        EZapiBootstrap.WriteDiagnostics();
        return $"测试供应商排队结果：{queued.Status}{Reason(queued)}；肖像键：{spriteName}。回调不会扣款、发货或写存档。";
    }

    internal static string Status()
    {
        if (_request is null) return "尚未排入测试供应商。";
        var status = SupplierApi.Status(Owner, _request);
        WriteReport(status);
        EZapiBootstrap.WriteDiagnostics();
        return $"测试供应商：{status.Status}{Reason(status)}；报价页 {_captureCalls} 次，确认回调 {_purchaseCalls} 次，离场回调 {_dismissCalls} 次。";
    }

    internal static void Maintain()
    {
        if (_request is null || !GameSession.OnThread) return;
        var now = Environment.TickCount64;
        if (now < _nextCheckAt) return;
        _nextCheckAt = now + 500;
        var status = SupplierApi.Status(Owner, _request);
        var stamp = $"{status.Status}|{status.Reason}|{_captureCalls}|{_purchaseCalls}|{_dismissCalls}";
        if (stamp == _stamp) return;
        WriteReport(status);
        EZapiBootstrap.WriteDiagnostics();
    }

    internal static void SceneChanged()
    {
        _request = _spriteName = _stamp = null;
        _identity = null;
        _queueResult = null;
        _nextCheckAt = 0;
        _captureCalls = _purchaseCalls = _dismissCalls = 0;
        LastReport = null;
    }

    internal static void Clear() => SceneChanged();

    private static SupplierPage Capture(DialogueContext context)
    {
        var page = ++_captureCalls;
        return new SupplierPage($"EZapi 原生供应商验收 · 报价页 {page}。此测试只验证访客与对话，不会扣款或交付物品。", new[]
        {
            new SupplierOffer(OfferId, "安全测试报价", "确认后只增加本地诊断计数，不修改玩家资源。", _ =>
            {
                _purchaseCalls++;
                return $"测试报价已确认；累计确认回调：{_purchaseCalls}。未扣款、未发货。";
            }, "只记录回调")
        });
    }

    private static string? FindLoadedNativeSprite()
    {
        try
        {
            var clients = PlayerStore.instance?.storeClientManager?.clientStack;
            if (clients is null) return null;
            for (var i = clients.Count - 1; i >= 0; i--)
            {
                var client = clients[i];
                if (client is null || client.Pointer == IntPtr.Zero) continue;
                var sprite = client.spriteName;
                if (!string.IsNullOrWhiteSpace(sprite)) return sprite;
            }
        }
        catch (Exception error)
        {
            MelonLoader.MelonLogger.Warning("[EZapi] EZAPI_DEBUG_SUPPLIER_SPRITE: " + error);
        }
        return null;
    }

    private static string Reason(PresentationResult result) => string.IsNullOrEmpty(result.Reason) ? string.Empty : " (" + result.Reason + ")";

    private static void WriteReport(PresentationResult status)
    {
        LastReport = new
        {
            at = DateTimeOffset.Now,
            owner = Owner,
            request = _request,
            identity = _identity,
            spriteName = _spriteName,
            queueStatus = _queueResult?.Status.ToString(),
            queueReason = _queueResult?.Reason,
            status = status.Status.ToString(),
            reason = status.Reason,
            captureCalls = _captureCalls,
            purchaseCalls = _purchaseCalls,
            dismissCalls = _dismissCalls,
            sideEffects = "none; no payment, item delivery, inventory mutation or save write"
        };
        _stamp = $"{status.Status}|{status.Reason}|{_captureCalls}|{_purchaseCalls}|{_dismissCalls}";
    }
}
