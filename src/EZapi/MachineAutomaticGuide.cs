namespace EZapi;

/// <summary>仅转换静态描述；不创建原生物品，不调用作者Condition/Resolve/Prepare。</summary>
internal static class MachineAutomaticGuide
{
    internal static IReadOnlyList<RecipeGuideEntry> Build(MachineDefinition definition,IReadOnlyList<RegisteredMachineRecipe> recipes)
    {
        var rows=new List<RecipeGuideEntry>();
        foreach(var registered in recipes) {
            var r=registered.Recipe;var options=r.Guide??definition.Guide;if(options is null)continue;
            var inputs=new List<RecipeGuideFigure>();
            foreach(var i in r.ItemInputs)inputs.Add(Figure(i.ItemId,i.Count,i.Guide,i.WholeStack?"整堆，总计 "+i.Count:null,
                i.ItemTag is {} tag?"标签："+tag:null,i.WholeStack?"Whole stacks, total "+i.Count:null,i.ItemTag is {} et?"Tag: "+et:null));
            foreach(var i in r.LiquidInputs)inputs.Add(Figure(i.Guide?.IllustrationItemId??i.LiquidId,Math.Max(1,i.Millilitres),i.Guide,
                i.ResolveMillilitres is null?i.Millilitres+" ml":"动态用量（基准 "+i.Millilitres+" ml）","容器槽："+i.SlotId,
                i.ResolveMillilitres is null?i.Millilitres+" ml":"Dynamic amount (base "+i.Millilitres+" ml)","Container slot: "+i.SlotId));
            var output=r.Output;var row=new RecipeGuideEntry(registered.OwnerId,r.RecipeId,definition.MachineId,definition.ItemOptions?.Name??definition.MachineId,
                options.CategoryId,inputs,Figure(output is MachineContainerOutput?output.Guide?.IllustrationItemId??output.Contents.FirstOrDefault()?.LiquidId:output.ItemId,
                    output.Count,output.Guide,output is MachineContainerOutput?"液体（加入已有容器）":output.ResolveCount is null?null:"动态数量（基准 "+output.Count+"）",
                    output.ResolveItemId is null?null:"动态产物，以实际加工为准",
                    output is MachineContainerOutput?"Liquid (added to existing container)":output.ResolveCount is null?null:"Dynamic quantity (base "+output.Count+")",
                    output.ResolveItemId is null?null:"Dynamic product; actual batch determines result"),options.Note){
                        LocalizedMachineName=definition.ItemOptions?.LocalizedName,LocalizedNote=options.LocalizedNote};
            rows.Add(row);
        }
        // 按真实作者校验前缀与每作者数量，再交给统一图鉴目录检查跨分区冲突。
        return Array.AsReadOnly(rows.GroupBy(r=>r.OwnerId,StringComparer.Ordinal).SelectMany(g=>RecipeGuideRules.Copy(g.Key,g.ToArray())).ToArray());
    }
    private static RecipeGuideFigure Figure(string? itemId,long count,RecipeGuideDisplay? display,string? quantity,string? requirement,string? englishQuantity=null,string? englishRequirement=null)
    {
        var illustration=display?.IllustrationItemId??itemId;
        if(illustration is null)throw new ArgumentException("EZAPI_MACHINE_GUIDE: tag-only/mixture ingredient needs Guide.IllustrationItemId, or disable automatic guide.");
        return new(illustration,count,display?.Name,display?.Requirement??requirement,display?.Quantity??quantity){
            LocalizedName=display?.LocalizedName,
            LocalizedRequirement=display?.LocalizedRequirement??(display?.Requirement is null&&requirement is not null?new(requirement,englishRequirement):null),
            LocalizedQuantity=display?.LocalizedQuantity??(display?.Quantity is null&&quantity is not null?new(quantity,englishQuantity):null)};
    }
}
