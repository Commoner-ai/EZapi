using EZapi;
using MelonLoader;

[assembly: MelonInfo(typeof(Author.Tooltip.Mod), "Native Tooltip Example", "1.0.0", "Author")]
[assembly: MelonGame(null, null)]
[assembly: MelonAdditionalDependencies("EZapi")]
namespace Author.Tooltip;

/// <summary>Declares a material; does not install, deliver items or save. 只声明普通材料。</summary>
public sealed class Mod : MelonMod
{
    public override void OnInitializeMelon()
    {
        var result = ItemRegistry.Register(new ItemDefinition {
            Id = "author.tooltip_dll.processor", Name = "Example processor",
            LocalizedName = new("示例处理器", "Example processor"),
            // MATERIAL is a gameplay assumption for this example. 按真实玩法选择分类。
            Types = new[] { "MATERIAL" }, Width = 1, Height = 1, Value = 20,
            DescriptionPlacement = "native",
            FlavorText = "A trace of warmth lingers in the old silicon.",
            LocalizedFlavorText = new("旧硅片仍有余温。", "A trace of warmth lingers in the old silicon."),
            Tooltip = new() { ShowSource = false }
        });
        // Accepted is not native Applied state or delivery. 接收不等于应用或发放。
        if (!result.Accepted) LoggerInstance.Error(result.Message);
    }
}
