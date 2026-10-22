namespace EZapi.Sample;

/// <summary>按需调用Register一次；框架负责地图生命周期和空位。此示例只写日志，不消耗次数或保存。</summary>
public static class DepartureExamples
{
    private const string Owner = "author.departure", Id = Owner + ".destination";
    private static readonly ModLog Log = new(Owner);
    public static PresentationResult Register() => DepartureApi.Register(Owner,
        new DepartureButton(Id, "Example destination", context =>
            Log.Info("Departure clicked: slot=" + context.SlotId + ", run=" + context.RunId))
        {
            DisplayName = context => context.Language.Resolve(new LocalizedItemText("示例外出地点", "Example destination")),
            Visible = context => true, // 只读查询；实际项目可读取自己的解锁进度。
            CanEnter = context => true, // 点击时重新检查；不要在这里扣款或发物品。
            Order = 100
        });
    public static PresentationResult Unregister() => DepartureApi.Unregister(Owner, Id);
}
