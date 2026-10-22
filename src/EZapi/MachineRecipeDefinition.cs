using Il2Cpp;
namespace EZapi;

/// <summary>固定文本图鉴覆盖。动态配方应提供Quantity/Requirement，图鉴不会调用任何解析器。</summary>
public sealed record RecipeGuideDisplay(string? IllustrationItemId=null,string? Name=null,string? Requirement=null,string? Quantity=null)
{
    public LocalizedItemText? LocalizedName {get;init;}
    public LocalizedItemText? LocalizedRequirement {get;init;}
    public LocalizedItemText? LocalizedQuantity {get;init;}
    internal void Validate(){LocalizedName?.Validate(200);LocalizedRequirement?.Validate(1000);LocalizedQuantity?.Validate(200);}
}
public sealed record RecipeGuideOptions(string CategoryId="synthesis",string? Note=null)
{
    public LocalizedItemText? LocalizedNote {get;init;}
}
/// <summary>追加配方保留真实作者；Recipe集合经深复制，不包含运行中的原生实例。</summary>
public sealed record RegisteredMachineRecipe(string OwnerId,MachineRecipe Recipe);

/// <summary>ID与Tag同时填写时取交集；Condition只读。WholeStack要求所选整堆之和恰为Count，不多扣。</summary>
public sealed record MachineIngredient(string? ItemId,int Count=1,Func<GameItem,bool>? Condition=null)
{
    public string? ItemTag {get;init;}
    public bool WholeStack {get;init;}
    public RecipeGuideDisplay? Guide {get;init;}
}
/// <summary>数量为整数ml；1000parts=1ml。相同槽可重复声明，按顺序从剩余成分扣算，不重复使用液体。</summary>
public sealed record MachineLiquidIngredient(string SlotId,int Millilitres,string? LiquidId=null,
    Func<GameItem,bool>? Condition=null,Func<MachineBatchContext,int>? ResolveMillilitres=null)
{
    public RecipeGuideDisplay? Guide {get;init;}
}
/// <summary>每件产物的内容物计划。Value/QualityBasis为该成分合计；默认事务自动填充，自定义事务由PrepareItem填充，提交后核验。</summary>
public sealed record MachineLiquidAmount(string LiquidId,int Millilitres,
    Func<MachineBatchContext,int>? ResolveMillilitres=null,Func<MachineBatchContext,decimal>? ResolveValue=null)
{
    public Func<MachineBatchContext,decimal>? ResolveQualityBasis {get;init;}
}
public record MachineItemOutput(string ItemId,int Count=1,
    Func<MachineBatchContext,int>? ResolveCount=null,Action<MachineRecipePlan,GameItem>? PrepareItem=null)
{
    public RecipeGuideDisplay? Guide {get;init;}
    public Func<MachineBatchContext,string>? ResolveItemId {get;init;}
    public IReadOnlyList<MachineLiquidAmount> Contents {get;init;}=Array.Empty<MachineLiquidAmount>();
}
/// <summary>
/// 向机器container输出槽中已有的容器添加整批液体，不创建/交付新物品。
/// 继承旧输出声明以保留MachineRecipe构造签名；请勿设置继承的ItemId/Count/PrepareItem等物品专用成员。
/// ResolveContents返回整批原生parts及合计价值/品质，覆盖固定Contents；Condition只读检查已有输出容器。
/// </summary>
public sealed record MachineContainerOutput:MachineItemOutput
{
    public MachineContainerOutput(IReadOnlyList<MachineLiquidAmount> contents):base("")=>Contents=contents;
    public Func<MachineBatchContext,IReadOnlyList<MachineLiquidPart>>? ResolveContents {get;init;}
    public Func<GameItem,bool>? Condition {get;init;}
}
/// <summary>显式整批/每件费用。null规则读取原生机器电耗；自定义规则不再乘原生效率。</summary>
public sealed record MachinePowerRule(int Cost,Func<MachineBatchContext,int>? ResolveCost=null,bool PerOutput=false);
/// <summary>标准事务的容器材料策略。默认拒绝内装物；ConsumeGraph明确消费根材料及所有后代，失败完整退回。</summary>
public enum MachineMaterialContentsPolicy { Reject, ConsumeGraph }
/// <summary>
/// 可执行配方。所有条件/解析器/事务工厂只读；PrepareItem只配置新产物。
/// Transaction省略时用框架标准消费/补偿/冻结事务；指定后由作者负责资源写入与审计。
/// </summary>
public sealed record MachineRecipe(string RecipeId,IReadOnlyList<MachineIngredient> ItemInputs,MachineItemOutput Output)
{
    /// <summary>仅框架标准Transaction使用；含子物品材料只能整根消费，不能拆分其堆叠。自定义事务自行定义内容物去向。</summary>
    public MachineMaterialContentsPolicy MaterialContents {get;init;}=MachineMaterialContentsPolicy.Reject;
    public RecipeGuideOptions? Guide {get;init;}
    public Func<GameItem,bool>? MachineCondition {get;init;}
    public string RuleRevision {get;init;}="1";
    public IReadOnlyList<MachineLiquidIngredient> LiquidInputs {get;init;}=Array.Empty<MachineLiquidIngredient>();
    public IReadOnlyList<string> AuxiliaryItemIds {get;init;}=Array.Empty<string>();
    public Func<MachineRecipePlan,IMachineBatchTransaction>? Transaction {get;init;}
    /// <summary>仅确认提交后一次；异常记录CompletionError，不回滚、不自动重试。可在此显式触发工作效果。</summary>
    public Action<MachineRecipePlan,IReadOnlyList<GameItem>>? AfterCommitted {get;init;}
}
/// <summary>仅本次同步调用有效，不能跨帧缓存。落点在全部产物配置后、Apply前填入；不代表空间已预留。</summary>
public sealed class MachineRecipePlan
{
    internal MachineRecipePlan(MachineRecipe recipe,MachineBatchContext context,string itemId,int cost,
        IReadOnlyList<MachineLiquidPart> contents,MachineInventory inventory)
        =>(Recipe,Context,OutputItemId,PowerCost,OutputContents,Inventory)=(recipe,context,itemId,cost,contents,inventory);
    public MachineRecipe Recipe {get;}
    public MachineBatchContext Context {get;}
    public MachineInventory Inventory {get;}
    public string OutputItemId {get;}
    public int PowerCost {get;}
    public IReadOnlyList<MachineLiquidPart> OutputContents {get;}
    /// <summary>仅容器输出有值；当前同步调用内借用，不能缓存。新增液体而非清空重灌。</summary>
    public GameItem? OutputContainer {get;internal set;}
    public MachineLiquidSnapshot? OutputContainerBefore {get;internal set;}
    public MachineLiquidSnapshot? OutputContainerAfter {get;internal set;}
    public IReadOnlyList<InventoryPlacement> OutputPlacements {get;internal set;}=Array.Empty<InventoryPlacement>();
}
public sealed record MachineBatchResult(string? RecipeId,string? Error,MachineTransactionResult? Transaction)
{
    public bool Committed=>Transaction?.Committed==true;
}
/// <summary>纯托管目录描述；不暴露委托或原生引用。Revision是目录修订，不是夜间去重号。</summary>
public sealed record MachineRecipeView(string OwnerId,string MachineId,string RecipeId,string RuleRevision,int Priority);
public sealed record MachineRecipeSnapshot(long Revision,IReadOnlyList<MachineRecipeView> Recipes);

internal static class MachineRecipeRules
{
    internal static void Id(string? id) {if(string.IsNullOrWhiteSpace(id)||id.Length>160||id.Any(char.IsControl))throw Invalid("readable ID<=160 required");}
    internal static void Owner(string owner) {if(owner is null||owner.Length>160||!System.Text.RegularExpressions.Regex.IsMatch(owner,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))throw Invalid("full lowercase owner required");}
    internal static MachineRecipe[] Copy(string owner,IReadOnlyList<MachineRecipe> recipes)
    {
        Owner(owner);ArgumentNullException.ThrowIfNull(recipes);
        if(recipes.Count>64)throw Invalid("max64 recipes");
        var ids=new HashSet<string>(StringComparer.Ordinal);var result=new List<MachineRecipe>();
        foreach(var r in recipes) {
            if(r is null)throw Invalid("null recipe");Id(r.RecipeId);Id(r.RuleRevision);
            if(r.MaterialContents is not (MachineMaterialContentsPolicy.Reject or MachineMaterialContentsPolicy.ConsumeGraph))throw Invalid("unknown material contents policy");
            if(!r.RecipeId.StartsWith(owner+".",StringComparison.Ordinal)||r.RecipeId.Length<=owner.Length+1||!ids.Add(r.RecipeId))throw Invalid("unique owner-prefixed recipe ID required");
            if(r.ItemInputs is null||r.LiquidInputs is null||r.AuxiliaryItemIds is null||r.Output?.Contents is null||
                r.ItemInputs.Count>16||r.LiquidInputs.Count>16||r.AuxiliaryItemIds.Count>64||r.Output.Contents.Count>32)throw Invalid("recipe collection limits:16/16/64/32");
            var items=r.ItemInputs.ToArray();var liquids=r.LiquidInputs.ToArray();var auxiliary=r.AuxiliaryItemIds.ToArray();var contents=r.Output.Contents.ToArray();
            foreach(var i in items) {if(i is null||i.Count is <1 or >1000000000||i.ItemId is null&&i.ItemTag is null)throw Invalid("bounded quantity and ID or tag required");if(i.ItemId is not null)Id(i.ItemId);if(i.ItemTag is not null)Id(i.ItemTag);}
            foreach(var i in liquids) {if(i is null||i.Millilitres is <0 or >2147483)throw Invalid("liquid ml outside0..2147483");Id(i.SlotId);if(i.LiquidId is not null)Id(i.LiquidId);}
            foreach(var id in auxiliary)Id(id);
            if(auxiliary.Distinct(StringComparer.Ordinal).Count()!=auxiliary.Length)throw Invalid("duplicate auxiliary ID");
            foreach(var i in contents) {if(i is null||i.Millilitres is <0 or >2147483)throw Invalid("output liquid ml outside0..2147483");Id(i.LiquidId);}
            if(contents.Select(i=>i.LiquidId).Distinct(StringComparer.Ordinal).Count()!=contents.Length)throw Invalid("duplicate output component");
            foreach(var i in items)i.Guide?.Validate();foreach(var i in liquids)i.Guide?.Validate();r.Output.Guide?.Validate();r.Guide?.LocalizedNote?.Validate(4000);
            if(r.Output is MachineContainerOutput) {
                if(r.Output.ItemId!=""||r.Output.Count!=1||r.Output.ResolveCount is not null||r.Output.ResolveItemId is not null||r.Output.PrepareItem is not null)
                    throw Invalid("container output cannot set item-only ID/count/prepare resolvers");
            } else {Id(r.Output.ItemId);if(r.Output.Count is <1 or >64)throw Invalid("1..64 independent output items required");}
            result.Add(r with {ItemInputs=Array.AsReadOnly(items),LiquidInputs=Array.AsReadOnly(liquids),AuxiliaryItemIds=Array.AsReadOnly(auxiliary),Output=r.Output with {Contents=Array.AsReadOnly(contents)}});
        }
        return result.ToArray();
    }
    internal static ArgumentException Invalid(string reason)=>new("EZAPI_RECIPE_DEFINITION: "+reason);
}
