using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>布局和图鉴示例。本类不自动登记或发物；真实加工需要后续机器运行时或内容自己的事务。</summary>
public static class MachineGuideExamples
{
    /// <summary>在你的目录工厂内部、SetShape/资源初始化之后调用。每次创建物品都必须调用。</summary>
    public static MachineInventory AddLayoutInFactory(GameItem freshMachine)
        =>MachineLayoutApi.Create(freshMachine,new MachineLayoutDefinition {
            LocalizedTitle=new("示例装配台","Example assembler"),ItemInput=new(4,4),Output=new(){Kind="items",Size=new(4,4)},
            Battery=new(new(2,2)),Modules=new(){Size=new(4,4),AllowedTypes=new[]{"MODULE_TYPE_UNIVERSAL"}},
            LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Size=new(2,3),LocalizedLabel=new("原料容器","Ingredient container")}},ManualSlot=true
        });

    /// <summary>只读说明不会创造可执行配方；以下物品ID由你的内容包另行注册。0.1.58起图片直接按ItemId解析，无需另写图片字段。</summary>
    public static ContentSubmissionResult RegisterGuide()
        =>RecipeGuideApi.RegisterRecipes("author.tools",new[]{new RecipeGuideEntry(
            "author.tools","author.tools.assembly","author.tools.assembler","示例装配台","synthesis",
            new[]{new RecipeGuideFigure("author.tools.scrap",2,"废料","品质满足加工要求")},
            new("author.tools.part",1,"零件"),"材料和功耗由真实生产逻辑核对，这里只登记说明。")});

    /// <summary>店铺就绪、没有拖拽物品时调用；false表示窗口未打开，不能当作已展示。</summary>
    public static bool OpenGuide()=>RecipeGuideApi.Open();
    /// <summary>同一目录提供双语；游戏切语言自动刷新。字段可在JSON使用相同camelCase名称。</summary>
    public static ContentSubmissionResult RegisterBilingualGuide()
        =>RecipeGuideApi.RegisterRecipes("author.tools",new[]{new RecipeGuideEntry(
            "author.tools","author.tools.assembly","author.tools.assembler","Assembler","synthesis",
            new[]{new RecipeGuideFigure("author.tools.scrap",2){LocalizedName=new("废料","Scrap"),LocalizedRequirement=new("品质合格","Required quality met"),LocalizedQuantity=new("2份","2 units")}},
            new("author.tools.part",1){LocalizedName=new("零件","Part")}){
                LocalizedMachineName=new("示例装配台","Example assembler"),LocalizedNote=new("只显示说明，不执行加工。","Instructions only; does not execute a batch.")}});
}
