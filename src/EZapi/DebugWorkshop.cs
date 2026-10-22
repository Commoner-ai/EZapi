namespace EZapi;

/// <summary>独立工坊预览夹具：只切换托管快照状态，不扣资源、不生成奖励、不保存。</summary>
internal static class DebugWorkshop
{
    private const string Owner = "ezapi.debug";
    private const string Tab = Owner + ".workshop";
    private const string Entry = Owner + ".workshop.demo";
    private static bool _registered, _unlocked;
    private static int _actionCalls;
    internal static string LastReport { get; private set; } = "not run";

    internal static void Register()
    {
        var result = WorkshopApi.RegisterTab(Owner, Tab, "EZapi 工坊预览", "EZapi Workshop Preview", Capture, Unlock);
        _registered = result.Accepted;
        if (!_registered) MelonLoader.MelonLogger.Warning("[EZapi] Workshop fixture unavailable: " + result.Reason);
    }

    internal static string Open()
    {
        if (!_registered) Register();
        var opened = WorkshopApi.OpenTab(Tab);
        LastReport = opened ? "preview opened; all data is managed-only" : "preview could not open; check workshop diagnostics";
        return LastReport;
    }

    internal static void Reset() { _unlocked = false; _actionCalls = 0; }
    internal static void Clear() { _registered = false; Reset(); LastReport = "cleared"; }

    private static WorkshopSnapshot Capture()
    {
        var description = "这个演示只改变当前进程内的只读快照。\n" +
            string.Join("\n", Enumerable.Range(1, 24).Select(i => $"滚动验证第 {i:00} 行：只读说明，不扣款、不发奖。")) +
            "\n滚动末尾标记：EZAPI-WORKSHOP-END";
        var englishDescription = "This demo changes only an in-memory view.\n" +
            string.Join("\n", Enumerable.Range(1,24).Select(i => $"Scroll check {i:00}: display only; no payment or rewards.")) +
            "\nEnd marker: EZAPI-WORKSHOP-END";
        var node = new WorkshopEntry(Entry, "工坊演示节点", $"动作调用 {_actionCalls} 次", description,
            "没有检查真实库存", "50 credits", "1 node", _unlocked ? "已解锁" : "未解锁",
            _unlocked ? "已完成" : "演示解锁", _unlocked, true, !_unlocked)
        {
            LocalizedTitle = new("工坊演示节点", "Workshop demo node"),
            LocalizedSubtitle = new($"动作调用 {_actionCalls} 次", $"Action calls: {_actionCalls}"),
            LocalizedDescription = new(description, englishDescription),
            LocalizedRequirement = new("没有检查真实库存", "Actual inventory is not evaluated"),
            LocalizedCostText = new("50 信用点", "50 credits"),
            LocalizedNodeCostText = new("1 节点", "1 node"),
            LocalizedStateText = new(_unlocked ? "已解锁" : "未解锁", _unlocked ? "Unlocked" : "Locked"),
            LocalizedActionText = new(_unlocked ? "已完成" : "演示解锁", _unlocked ? "Completed" : "Demo unlock"),
            Conditions = Array.AsReadOnly(new[] { new WorkshopRequirement(WorkshopResourceKind.Credits, "credits", 50, false, true) { DisplayName = "演示条件", LocalizedDisplayName = new("演示条件", "Demo condition") } }),
            Rewards = Array.AsReadOnly(new[] { new WorkshopReward(WorkshopResourceKind.Item, "ezapi.debug.reward", 1) { DisplayName = "演示奖励（不发放）", LocalizedDisplayName = new("演示奖励（不发放）", "Demo reward (not delivered)") } }),
            Completed = _unlocked,
            VisualLinks = Array.AsReadOnly(new[] { Owner + ".workshop.root" }),
            GraphX = 0,
            GraphY = 1
        };
        var root = new WorkshopEntry(Owner + ".workshop.root", "演示根节点", "仅用于连线", "无业务效果。", "", "", "", "已开放", "", true, true, false)
        { GraphX = 0, GraphY = 0, VisualLinks = Array.AsReadOnly(new[] { Entry }),
            LocalizedTitle = new("演示根节点", "Demo root node"), LocalizedSubtitle = new("仅用于连线", "Visual link only"),
            LocalizedDescription = new("无业务效果。", "No gameplay effects."), LocalizedStateText = new("已开放", "Available") };
        return WorkshopSnapshot.ForGameLanguage(1000,
            Array.AsReadOnly(new[] { new WorkshopGroup("调试示例", "Debug Example", 2) }),
            Array.AsReadOnly(new[] { root, node }),
            "") with{LocalizedMessage=new("仅预览工坊列表/图谱与单次回调；资金、物品和存档均不变.",
                "Preview only: in-memory workshop list/graph and one callback. No credits, items or save changes.")};
    }

    private static void Unlock(string id)
    {
        if (id == Entry) { _unlocked = true; _actionCalls++; }
        LastReport = "in-memory demo callback invoked; no payment, item delivery, or save";
    }
}
