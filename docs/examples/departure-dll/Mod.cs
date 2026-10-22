using EZapi;
using MelonLoader;

[assembly: MelonInfo(typeof(DepartureExampleMod), "EZapi Departure Example", "1.0.0", "Example author")]
[assembly: MelonGame(null, null)]

/// <summary>消费方示例，只引用发布的EZapi.dll；不包含或重建框架。按需安装此示例DLL。</summary>
public sealed class DepartureExampleMod : MelonMod
{
    private const string Owner = "author.departureexample", Id = Owner + ".destination";

    public override void OnInitializeMelon()
    {
        var result = DepartureApi.Register(Owner, new DepartureButton(Id, "Example destination", context =>
        {
            // 将这里替换为自己的目的地控制器Open()；本示例只记录一次日志。
            // 外出次数、库存转移、返回与保存由目的地Mod维护，不放到下面的查询回调。
            LoggerInstance.Msg("Departure callback: slot=" + context.SlotId + ", run=" + context.RunId);
        })
        {
            DisplayName = context => context.Language.Resolve(new LocalizedItemText("示例外出地点", "Example destination")),
            Visible = context => true,
            CanEnter = context => true,
            RequireVisit = true,
            Order = 100
        });
        LoggerInstance.Msg("Departure registration: " + result.Status + " " + result.Reason);
    }

    public override void OnDeinitializeMelon() => DepartureApi.Unregister(Owner, Id);
}
