using EZapi.Registration;

namespace EZapi.Sample;

/// <summary>
/// Minimal Workshop API example. Unlock toggles only in-memory demonstration state; it does not charge credits,
/// deliver the listed reward, or write a save. Real mods must perform those operations in their own callback.
/// </summary>
public static class WorkshopExamples
{
    private const string Owner = "ezapi.sample";
    private const string TabId = Owner + ".workshop";
    private const string EntryId = Owner + ".workshop.field_kit";
    private static bool _unlocked;

    public static SubmitResult Register()
        => WorkshopApi.RegisterTab(Owner, TabId, "示例工坊", "Example Workshop", Capture, Unlock);

    public static bool Open() => WorkshopApi.OpenTab(TabId);
    public static void Close() => WorkshopApi.CloseWindow();

    private static WorkshopSnapshot Capture()
    {
        var entry = new WorkshopEntry(EntryId, "野外工具包", "示例节点", "展示工坊条目与资源条件。",
            "这是说明文本，不会自动验证游戏库存。", "100", "1", _unlocked ? "已解锁" : "未解锁",
            _unlocked ? "完成" : "演示解锁", _unlocked, true, !_unlocked)
        {
            Dependencies = Array.AsReadOnly(new[] { Owner + ".workshop.root" }),
            Rewards = Array.AsReadOnly(new[]
            {
                new WorkshopReward(WorkshopResourceKind.Item, Owner + ".field_kit", 1)
                { DisplayName = "", LocalizedDisplayName=new("野外工具包（仅展示）","Field kit (display only)") }
            }),
            LocalizedTitle=new("野外工具包","Field kit"),
            LocalizedSubtitle=new("示例节点","Example node"),
            LocalizedDescription=new("展示工坊条目与资源条件。","Displays a workshop entry and resource conditions."),
            LocalizedRequirement=new("这是说明文本，不会自动验证游戏库存。","Display only; inventory is not checked automatically."),
            LocalizedCostText=new("100","100"),
            LocalizedNodeCostText=new("1","1"),
            LocalizedStateText=new(_unlocked?"已解锁":"未解锁",_unlocked?"Unlocked":"Locked"),
            LocalizedActionText=new(_unlocked?"完成":"演示解锁",_unlocked?"Complete":"Demo unlock"),
            Conditions = Array.AsReadOnly(new[]
            {
                new WorkshopRequirement(WorkshopResourceKind.Credits, "credits", 100, false, true)
                { DisplayName = "", LocalizedDisplayName=new("示例资金要求","Example credit requirement") }
            }),
            Completed = _unlocked,
            VisualLinks = Array.AsReadOnly(new[] { Owner + ".workshop.root" }),
            GraphX = 1,
            GraphY = 0
        };
        return WorkshopSnapshot.ForGameLanguage(500,
            Array.AsReadOnly(new[] { new WorkshopGroup("示例进度", "Example Progress", 1) }),
            Array.AsReadOnly(new[] { entry }),
            "") with{LocalizedMessage=new("示例数据。展示条件、消耗标记与奖励不会自动修改游戏状态.",
                "Example data only. Conditions, costs and rewards are not applied automatically.")};
    }

    private static void Unlock(string entryId)
    {
        if (entryId == EntryId) _unlocked = true;
    }
}
