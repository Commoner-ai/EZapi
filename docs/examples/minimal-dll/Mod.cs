using EZapi;
using MelonLoader;

[assembly: MelonInfo(typeof(Author.SimpleMod.Mod), "EZapi Simple Mod", "1.0.0", "Author")]
[assembly: MelonGame(null, null)]
[assembly: MelonAdditionalDependencies("EZapi")]

namespace Author.SimpleMod;

/// <summary>
/// Minimal independent DLL Mod. Initialization declares an item; it does not give it to the player.
/// 最小 DLL Mod：初始化只声明物品，不自动发放、写存档或接管拖拽。
/// </summary>
public sealed class Mod : MelonMod
{
    public override void OnInitializeMelon()
    {
        // Replace this stable namespaced ID before publishing. Saved items depend on it.
        // 发布前改成作者自己的稳定 ID；有玩家存档后不要随意改名。
        var result = ItemRegistry.Register(new ItemDefinition
        {
            Id = "author.simple.repair_kit",
            Name = "Repair kit",
            LocalizedName = new("维修工具包", "Repair kit"),
            ShortDescription = "An example item; no healing effect.",
            LocalizedShortDescription = new("示例物品，不提供治疗效果。", "An example item; no healing effect."),
            Width = 1, Height = 1, Value = 25
        });

        // Accepted is declaration acceptance, not directory Applied or player delivery.
        // Accepted 只表示声明接收；目录应用和游戏内生成分别确认。
        if (!result.Accepted) LoggerInstance.Error(result.Message);
        else LoggerInstance.Msg("Item declaration accepted: " + result.ItemId);
    }
}
