using System.Collections.ObjectModel;
using Il2Cpp;
namespace EZapi;

/// <summary>可执行配方目录。登记不会注册机器物品，也不会添加原生夜间回调或自动保存。</summary>
public static class MachineRecipeApi
{
    private sealed record Entry(string Owner,string Machine,MachineRecipe[] Recipes,MachinePowerRule? Power,int Markup, IReadOnlyDictionary<string,string>? Owners=null);
    private static readonly Dictionary<string,Entry> Entries=new(StringComparer.Ordinal);
    private static bool _started,_running;
    private static long _generation,_revision;
    internal static bool CanEdit=>_started&&!_running;
    internal static bool Ready=>_started;
    internal static bool HasCatalog(string machine)=>Entries.ContainsKey(machine);
    internal static void RemoveManaged(string machine){if(Entries.TryGetValue(machine,out var e)&&e.Owners is not null){Entries.Remove(machine);_revision++;}}
    internal static string? CheckManaged(string machine,int count)=>!CanEdit?"EZAPI_RECIPE_NOT_READY":
        Entries.Where(e=>e.Key!=machine).Sum(e=>e.Value.Recipes.Length)+count>2048?"EZAPI_RECIPE_LIMIT: max2048 registered recipes.":null;
    internal static void CommitManaged(string owner,string machine,IReadOnlyList<RegisteredMachineRecipe> recipes,MachinePowerRule? power,int markup)
    {
        Entries[machine]=new(owner,machine,recipes.Select(r=>r.Recipe).ToArray(),power,markup,recipes.ToDictionary(r=>r.Recipe.RecipeId,r=>r.OwnerId,StringComparer.Ordinal));_revision++;
    }
    internal static long Generation=>_generation;
    internal static bool Current(long generation)=>_started&&generation==_generation;
    internal static bool Enter(long generation) {if(!Current(generation)||_running)return false;_running=true;return true;}
    internal static void Exit()=>_running=false;
    internal static void Start(){_started=true;_generation++;}
    internal static void Stop(){_started=false;_generation++;Entries.Clear();_revision++;}
    internal static object Report()=>new{ready=_started,running=_running,revision=_revision,machines=Entries.Count,
        scope="synchronous recipe processing with default material escrow/freeze/mixture liquid debit or custom transaction; no nightly hook or saving"};

    /// <summary>每台机器一个owner；整组替换，空组移除。初始化/主线程提交，不能在生产回调里改变目录。</summary>
    public static ContentSubmissionResult RegisterRecipes(string ownerId,string machineId,IReadOnlyList<MachineRecipe> recipes,
        MachinePowerRule? power=null,int productionMarkupPercent=0)
    {
        GameSession.RequireThread();
        if(Entries.TryGetValue(machineId,out var managed)&&managed.Owners is not null)return new(false,"EZAPI_MACHINE_MANAGED: use RegisterAdditionalRecipes; unified catalog cannot be replaced.");
        if(!_started||_running)return new(false,"EZAPI_RECIPE_NOT_READY: stopped or processing.");
        try {
            MachineRecipeRules.Id(machineId);var copy=MachineRecipeRules.Copy(ownerId,recipes);CheckOptions(power,productionMarkupPercent);
            if(Entries.TryGetValue(machineId,out var old)&&old.Owner!=ownerId)return new(false,"EZAPI_RECIPE_OWNER: machine recipe catalog already owned.");
            if(Entries.Where(e=>e.Key!=machineId).Sum(e=>e.Value.Recipes.Length)+copy.Length>2048)return new(false,"EZAPI_RECIPE_LIMIT: max2048 registered recipes.");
            if(copy.Length==0)Entries.Remove(machineId);else Entries[machineId]=new(ownerId,machineId,copy,power,productionMarkupPercent);
            _revision++;return new(true,null);
        }catch(ArgumentException e){return new(false,e.Message);}
    }
    public static MachineRecipeSnapshot Snapshot()
    {
        GameSession.RequireThread();return new(_revision,Array.AsReadOnly(Entries.Values.OrderBy(e=>e.Machine,StringComparer.Ordinal)
            .SelectMany(e=>e.Recipes.Select((r,i)=>new MachineRecipeView(e.Owners?.GetValueOrDefault(r.RecipeId)??e.Owner,e.Machine,r.RecipeId,r.RuleRevision,i))).ToArray()));
    }
    /// <summary>
    /// 显式给登记机器的精确输入子库存安装AND准入，返回句柄由作者释放。
    /// 匹配当前配方ID/Tag/Condition或辅助ID；目录替换即时生效，移除目录后释放限制。保留原生拒绝。
    /// </summary>
    public static IDisposable InstallInputAdmission(string ownerId,string machineId)
    {
        GameSession.RequireThread();MachineRecipeRules.Owner(ownerId);MachineRecipeRules.Id(machineId);
        if(!_started||_running||!Entries.TryGetValue(machineId,out var entry)||entry.Owner!=ownerId)
            throw new InvalidOperationException("EZAPI_RECIPE_OWNER: register own catalog before installing admission.");
        var generation=_generation;
        return InventoryAdmissionApi.Register(ownerId,machineId,machineId+".ezapi.machine.input",(item,_)=>{
            if(!Current(generation)||!Entries.TryGetValue(machineId,out var current)||current.Owner!=ownerId)return true;
            return current.Recipes.Any(r=>r.AuxiliaryItemIds.Contains(item.identifier,StringComparer.Ordinal)||r.ItemInputs.Any(i=>
                (i.ItemId is null||i.ItemId==item.identifier)&&(i.ItemTag is null||item.IsTag(i.ItemTag))&&(i.Condition?.Invoke(item)??true)));
        });
    }
    /// <summary>独立处理器复制定义、不持有游戏实例；每次Process重新解析槽位。Stop/Start后旧处理器失效。</summary>
    public static MachineBatchProcessor CreateBatchProcessor(string ownerId,string machineId,IReadOnlyList<MachineRecipe> recipes,
        MachinePowerRule? power=null,int productionMarkupPercent=0)
    {
        GameSession.RequireThread();MachineRecipeRules.Id(machineId);CheckOptions(power,productionMarkupPercent);
        return new(ownerId,machineId,MachineRecipeRules.Copy(ownerId,recipes),power,productionMarkupPercent,_generation);
    }
    internal static MachineBatchProcessor CreateBoundProcessor(string ownerId,MachineDefinition definition)
    {
        GameSession.RequireThread();MachineRecipeRules.Id(definition.MachineId);CheckOptions(definition.Power,definition.ProductionMarkupPercent);
        var template=JsonMachineLayout.Copy(definition.Template);var recipes=MachineRecipeRules.Copy(ownerId,definition.Recipes);
        var liquidIds=template.LiquidInputs.Select(s=>s.SlotId).ToHashSet(StringComparer.Ordinal);
        if(recipes.Any(r=>(r.Output is MachineContainerOutput)!=(template.Output.Kind=="container")||r.LiquidInputs.Any(l=>!liquidIds.Contains(l.SlotId))))
            throw MachineRecipeRules.Invalid("processor recipes must match template output kind and liquid slot IDs");
        return new(ownerId,definition.MachineId,recipes,definition.Power,
            definition.ProductionMarkupPercent,_generation,template:template);
    }
    public static MachineBatchResult ProcessBatch(GameItem machine)
    {
        NativeAccess.RequireItem(machine);
        if(!Entries.TryGetValue(machine.identifier,out var e))return new(null,"EZAPI_RECIPE_MISSING: no recipe catalog for machine.",null);
        return new MachineBatchProcessor(e.Owner,e.Machine,e.Recipes,e.Power,e.Markup,_generation,e.Owners).Process(machine);
    }
    private static void CheckOptions(MachinePowerRule? power,int markup)
    {if(power?.Cost<0||markup is <0 or >1000000)throw MachineRecipeRules.Invalid("nonnegative power and markup0..1000000 required");}
}

/// <summary>
/// 声明顺序选择第一条资源满足的配方，匹配后只提交一次；提交失败不改选另一配方。
/// 条件/解析器只读。辅助ID只用于描述准入，不自动消费；本版不接管机器输入过滤器。
/// </summary>
public sealed class MachineBatchProcessor
{
    private readonly string _owner,_machineId;private readonly MachineRecipe[] _recipes;private readonly MachinePowerRule? _power;
    private readonly IReadOnlyDictionary<string,string>? _owners;
    private readonly MachineLayoutDefinition? _template;
    private readonly int _markup;private readonly long _generation;
    internal MachineBatchProcessor(string owner,string machineId,MachineRecipe[] recipes,MachinePowerRule? power,int markup,long generation,IReadOnlyDictionary<string,string>? owners=null,MachineLayoutDefinition? template=null)
        =>(_owner,_machineId,_recipes,_power,_markup,_generation,_owners,_template)=(owner,machineId,recipes,power,markup,generation,owners,template);
    public MachineBatchResult Process(GameItem machine)=>ProcessCore(machine,null);
    /// <summary>借用当前机器的真实子库存。支持没有EZapi布局标记的自定义窗口；不可传入背包、另一机器或重复槽位。</summary>
    public MachineBatchResult Process(GameItem machine,MachineInventory inventory)
    {ArgumentNullException.ThrowIfNull(inventory);return ProcessCore(machine,inventory);}
    private MachineBatchResult ProcessCore(GameItem machine,MachineInventory? supplied)
    {
        var run=NativeAccess.RequireRun();NativeAccess.RequireItem(machine);
        if(!MachineRecipeApi.Enter(_generation))return new(null,"EZAPI_RECIPE_REENTRANT_OR_STALE",null);
        string? selected=null;MachineAutomationContext? automation=null;
        try {
            if(machine.identifier!=_machineId||machine.unitCount!=1||!GeneralHelper.IsItemOwned(machine)||InventoryApi.IsItemDragActive)
                return new(null,"EZAPI_RECIPE_MACHINE: matching owned single machine required.",null);
            var binding=MachineInventoryBinding.Resolve(machine,supplied,_template);
            if(binding is null)return new(null,"EZAPI_RECIPE_INVENTORY: actual distinct child inventories matching the processor template required.",null);
            var slots=binding.Slots;
            if(MachineFaultGuard.Blocked(machine))return new(null,"EZAPI_MACHINE_QUARANTINED: previous recovery failure; manual recovery required.",null);
            var output=slots.Output.TryCast<GameGridInventory>();
            var outputSlot=slots.Output.TryCast<GameSlotInventory>();
            if(output is null&&outputSlot is null)return new(null,"EZAPI_RECIPE_OUTPUT: item grid or container slot required.",null);
            var outputContainer=Single(outputSlot);
            var outputBefore=outputContainer is null?null:Liquids.CaptureProduction(outputContainer);
            var outputPointer=outputContainer?.Pointer;var outputInstance=outputContainer?.uniqueId;
            var outputState=outputContainer?.state.Pointer;var outputDict=outputContainer?.state.dict.Pointer;
            bool OutputCurrent()=>outputContainer is null||outputContainer.Pointer==outputPointer&&outputContainer.uniqueId==outputInstance&&
                outputContainer.unitCount==1&&outputContainer.state.Pointer==outputState&&outputContainer.state.dict.Pointer==outputDict&&
                outputContainer.parentInventory?.Pointer==slots.Output.Pointer;
            var inventoryBefore=Capture(slots,run);
            var inputs=new List<GameItem>();foreach(var item in slots.ItemInput.childItems)inputs.Add(item);
            var stock=inputs.OrderBy(i=>i.uniqueId).ToArray();
            if(stock.Length>64)return new(null,"EZAPI_RECIPE_LIMIT: max64 input stacks.",null);
            var materialGraphs=stock.Select(MachineMaterialGraph.CaptureState).ToArray();
            // 只读选择/Prepare阶段观察整台机器，含普通材料标签和非输入槽资源。
            // Apply之后图必然改变，不能拿这份选择快照拒绝合法消耗；Restore沿用独立资源日记检查。
            var readonlyState=MachineMaterialGraph.CaptureState(machine);
            var values=stock.Select(i=>i.unitValue).ToArray();
            if(values.Any(v=>v<0))return new(null,"EZAPI_RECIPE_VALUE: negative native base value.",null);
            var battery=Single(slots.Battery);var energy=battery is null?0:Power.GetEnergy(battery);
            bool RequiredSourceAvailable()=>!slots.RequiresBattery||battery is not null&&GeneralHelper.IsItemOwned(battery)&&!InventoryFreezeApi.IsFrozen(battery)&&PowerHelper.CanDrawPowerSource(battery,0);
            if(!RequiredSourceAvailable())
                return new(null,"EZAPI_RECIPE_BATTERY_REQUIRED: layout requires an available owned power source even for zero power cost.",null);
            var machinePointer=machine.Pointer;var machineInstance=machine.uniqueId;var parent=machine.parentInventory?.Pointer??IntPtr.Zero;
            var allLiquids=new Dictionary<string,GameItem>(StringComparer.Ordinal);
            foreach(var s in slots.LiquidInputs)if(Single(s.Value) is { } item)allLiquids.Add(s.Key,item);
            var liquidBefore=allLiquids.ToDictionary(e=>e.Key,e=>Liquids.CaptureProduction(e.Value),StringComparer.Ordinal);
            bool Current()=>MachineRecipeApi.Current(_generation)&&NativeAccess.SameRun(run)&&machine.Pointer==machinePointer&&machine.uniqueId==machineInstance&&
                machine.identifier==_machineId&&machine.unitCount==1&&GeneralHelper.IsItemOwned(machine)&&(machine.parentInventory?.Pointer??IntPtr.Zero)==parent&&binding.Current(machine);
            bool UnchangedExceptEnergy()=>Current()&&!InventoryApi.IsItemDragActive&&Same(inventoryBefore,Capture(slots,run))&&
                stock.Select(i=>i.unitValue).SequenceEqual(values)&&allLiquids.All(e=>LiquidMetadata.Same(liquidBefore[e.Key],Liquids.CaptureProduction(e.Value)))&&
                OutputCurrent()&&(outputContainer is null||LiquidMetadata.Same(outputBefore!,Liquids.CaptureProduction(outputContainer)))&&materialGraphs.All(g=>g!.Matches());
            bool Unchanged()=>UnchangedExceptEnergy()&&readonlyState.Matches()&&(battery is null||Power.GetEnergy(battery)==energy);
            automation=MachineAutomationApi.Create(machine,slots,Unchanged);
            MachineAutomationApi.Dispatch(_machineId,MachineAutomationPhase.BeforeBatch,automation);
            if(!automation.IsCurrent)return new(null,"EZAPI_MACHINE_AUTOMATION_STALE: before callback changed resources or scope.",null);
            if(automation.IsBlocked)return new(null,"EZAPI_MACHINE_AUTOMATION_BLOCKED",null);
            var requested=automation.Selection;
            foreach(var recipe in _recipes) {
                if(requested is not null&&recipe.RecipeId!=requested)continue;
                var containerSpec=recipe.Output as MachineContainerOutput;
                if((containerSpec is not null)!=(outputSlot is not null))continue;
                if(containerSpec is not null&&(outputContainer is null||!GeneralHelper.IsItemOwned(outputContainer)||InventoryFreezeApi.IsFrozen(outputContainer)||
                    !MachineLayoutApi.Allows(outputContainer,slots.Output)||!binding.AllowsOutput(outputContainer)||containerSpec.Condition?.Invoke(outputContainer)==false))continue;
                if(!Unchanged())return new(null,"EZAPI_RECIPE_STALE: inputs changed during read-only resolution.",null);
                if(recipe.MachineCondition?.Invoke(machine)==false)continue;
                var eligible=new bool[recipe.ItemInputs.Count,stock.Length];
                for(var r=0;r<recipe.ItemInputs.Count;r++)for(var i=0;i<stock.Length;i++) {
                    var spec=recipe.ItemInputs[r];var item=stock[i];
                    eligible[r,i]=(spec.ItemId is null||item.identifier==spec.ItemId)&&(spec.ItemTag is null||item.IsTag(spec.ItemTag))&&
                        (spec.Condition?.Invoke(item)??true)&&GeneralHelper.IsItemOwned(item)&&!InventoryFreezeApi.IsFrozen(item);
                }
                var allocation=MachineIngredientMatching.Allocate(stock.Select(i=>i.unitCount).ToArray(),recipe.ItemInputs.Select(i=>i.Count).ToArray(),recipe.ItemInputs.Select(i=>i.WholeStack).ToArray(),eligible);
                if(allocation is null)continue;
                var takes=stock.Select((item,i)=>new MachineItemTake(item,allocation[i]){Value=checked((decimal)values[i]*allocation[i])}).Where(t=>t.Count>0).ToArray();
                var context=new MachineBatchContext(machine,takes,new ReadOnlyDictionary<string,GameItem>(allLiquids),1){ProductionMarkupPercent=_markup};
                var remaining=new Dictionary<string,MachineLiquidSnapshot>(liquidBefore,StringComparer.Ordinal);var used=new List<MachineLiquidTake>();var missing=false;
                foreach(var ingredient in recipe.LiquidInputs) {
                    if(!allLiquids.TryGetValue(ingredient.SlotId,out var container)||!MachineLayoutApi.Allows(container,slots.LiquidInputs[ingredient.SlotId])||!binding.AllowsLiquid(ingredient.SlotId,container)||ingredient.Condition?.Invoke(container)==false||InventoryFreezeApi.IsFrozen(container)){missing=true;break;}
                    var ml=ingredient.ResolveMillilitres?.Invoke(context with {Liquids=used})??ingredient.Millilitres;
                    if(ml is <0 or >2147483)throw MachineRecipeRules.Invalid("resolved liquid ml outside0..2147483");
                    var before=remaining[ingredient.SlotId];var parts=checked(ml*1000);
                    if(parts>(ingredient.LiquidId is null?before.TotalParts:before.Contents.FirstOrDefault(p=>p.LiquidId==ingredient.LiquidId)?.Parts??0)){missing=true;break;}
                    var after=parts==0?before:LiquidCompositionMath.Consume(before,parts,ingredient.LiquidId);
                    var consumed=LiquidCompositionMath.Consumed(before,after);
                    used.Add(new(ingredient.SlotId,container,before,after,LiquidCompositionMath.BaseValue(consumed)){QualityBasis=LiquidCompositionMath.Quality(consumed)});
                    remaining[ingredient.SlotId]=after;
                }
                if(missing)continue;
                context=context with {Liquids=used};
                var count=recipe.Output.ResolveCount?.Invoke(context)??recipe.Output.Count;
                if(count is <1 or >64)throw MachineRecipeRules.Invalid("resolved output count outside1..64");
                context=context with {OutputCount=count};
                var outputId=containerSpec is null?recipe.Output.ResolveItemId?.Invoke(context)??recipe.Output.ItemId:outputContainer!.identifier;
                MachineRecipeRules.Id(outputId);
                var cost=_power is null?Power.GetMachineCost(machine):MachinePowerMath.CalculateCost(_power.ResolveCost?.Invoke(context)??_power.Cost,count,_power.PerOutput);
                if(cost<0)throw MachineRecipeRules.Invalid("negative native power cost");
                if(cost>0&&(battery is null||energy<cost||!PowerHelper.CanDrawPowerSource(battery,cost)))continue;
                var resolvedContents=containerSpec?.ResolveContents?.Invoke(context);
                if(containerSpec?.ResolveContents is not null&&(resolvedContents is null||resolvedContents.Count>32))throw MachineRecipeRules.Invalid("resolved contents require nonnull collection<=32");
                var contents=resolvedContents?.ToArray()??recipe.Output.Contents.Select(c=>{
                    var ml=c.ResolveMillilitres?.Invoke(context)??c.Millilitres;
                    if(ml is <0 or >2147483)throw MachineRecipeRules.Invalid("resolved output liquid outside0..2147483ml");
                    return new MachineLiquidPart(c.LiquidId,checked(ml*1000)){Value=c.ResolveValue?.Invoke(context),QualityBasis=c.ResolveQualityBasis?.Invoke(context)};
                }).ToArray();
                if(contents.Length>32)throw MachineRecipeRules.Invalid("max32 output liquid components");
                _=new MachineLiquidSnapshot(int.MaxValue,contents); // 验证总量/元数据，尚不创建或写入产物。
                MachineLiquidSnapshot? outputAfter=null;
                if(containerSpec is not null) {
                    // 容量不足属于未匹配，允许继续下一条更小配方；非法目录/元数据仍明确报错。
                    if(contents.Sum(p=>(long)p.Parts)==0)throw MachineRecipeRules.Invalid("container output must produce positive liquid volume");
                    if(contents.Sum(p=>(long)p.Parts)>outputBefore!.CapacityParts-outputBefore.TotalParts)continue;
                    outputAfter=LiquidCompositionMath.Add(outputBefore,contents);
                }
                selected=recipe.RecipeId;
                var plan=new MachineRecipePlan(recipe,context,outputId,cost,Array.AsReadOnly(contents),slots) {
                    OutputContainer=containerSpec is null?null:outputContainer,OutputContainerBefore=containerSpec is null?null:outputBefore,OutputContainerAfter=outputAfter
                };
                if(!Unchanged())return new(selected,"EZAPI_RECIPE_STALE: resolver changed resources.",null);
                using var standard=recipe.Transaction is null?new DefaultMachineTransaction(plan):null;
                var transaction=standard as IMachineBatchTransaction??recipe.Transaction!(plan)??throw MachineRecipeRules.Invalid("transaction factory returned null");
                var prepared=new List<GameItem>();
                bool EligibleNow() {
                    if(!Unchanged()||!RequiredSourceAvailable()||InventoryFreezeApi.IsFrozen(machine)||slots.ItemInput.IsRemoveLocked()||slots.Output.IsInsertLocked()||
                        recipe.MachineCondition?.Invoke(machine)==false)return false;
                    foreach(var take in takes)if(!take.Item.MayRemove()||take.Item.MaxNumRemove()<take.Count)return false;
                    for(var r=0;r<recipe.ItemInputs.Count;r++)for(var i=0;i<stock.Length;i++)if(eligible[r,i]&&allocation[i]>0) {
                        var spec=recipe.ItemInputs[r];if(spec.ItemTag is not null&&!stock[i].IsTag(spec.ItemTag)||spec.Condition?.Invoke(stock[i])==false)return false;
                    }
                    foreach(var input in recipe.LiquidInputs)if(!MachineLayoutApi.Allows(allLiquids[input.SlotId],slots.LiquidInputs[input.SlotId])||!binding.AllowsLiquid(input.SlotId,allLiquids[input.SlotId])||input.Condition?.Invoke(allLiquids[input.SlotId])==false)return false;
                    if(containerSpec is not null&&(outputContainer is null||!GeneralHelper.IsItemOwned(outputContainer)||InventoryFreezeApi.IsFrozen(outputContainer)||
                        !MachineLayoutApi.Allows(outputContainer,slots.Output)||!binding.AllowsOutput(outputContainer)||containerSpec.Condition?.Invoke(outputContainer)==false))return false;
                    if(cost>0&&(battery is null||!PowerHelper.CanDrawPowerSource(battery,cost)))return false;
                    if(prepared.Count>0)plan.OutputPlacements=InventoryPlacementApi.PlanWholeGrid(output!,prepared);
                    return Unchanged();
                }
                bool PhysicalAfter() {
                    if(!Current()||prepared.Count!=(containerSpec is null?count:0)||prepared.Any(p=>p.parentInventory?.Pointer!=slots.Output.Pointer)||
                        battery is not null&&Power.GetEnergy(battery)!=energy-cost)return false;
                    foreach(var pair in remaining)if(!LiquidMetadata.Same(pair.Value,Liquids.CaptureProduction(allLiquids[pair.Key])))return false;
                    if(containerSpec is not null)return outputContainer is not null&&OutputCurrent()&&GeneralHelper.IsItemOwned(outputContainer)&&LiquidMetadata.Same(outputAfter!,Liquids.CaptureProduction(outputContainer));
                    if(contents.Length>0)foreach(var product in prepared) {
                        var actual=Liquids.CaptureProduction(product);var byId=contents.ToDictionary(c=>c.LiquidId,StringComparer.Ordinal);
                        if(actual.TotalParts!=contents.Sum(c=>(long)c.Parts))return false;
                        foreach(var part in actual.Contents) {
                            if(!byId.TryGetValue(part.LiquidId,out var expected)){if(part.Parts>0)return false;continue;}
                            if(part.Parts!=expected.Parts||expected.Value.HasValue&&part.Value!=expected.Value||expected.QualityBasis.HasValue&&part.QualityBasis!=expected.QualityBasis)return false;
                        }
                        if(contents.Any(c=>c.Parts>0&&!actual.Contents.Any(p=>p.LiquidId==c.LiquidId)))return false;
                    }
                    return true;
                }
                var guarded=new Guarded(transaction,EligibleNow,PhysicalAfter,()=>UnchangedExceptEnergy()&&(standard?.RestoredExternalPower==true||battery is null||Power.GetEnergy(battery)==energy));
                var productDefinitions=containerSpec is not null?Array.Empty<MachineProductDefinition>():new[]{new MachineProductDefinition(outputId,count,(_,product)=>{
                    prepared.Add(product);standard?.Prepare(product);recipe.Output.PrepareItem?.Invoke(plan,product);
                })};
                var result=MachineTransactionApi.Run(_owners?.GetValueOrDefault(recipe.RecipeId)??_owner,context,productDefinitions,guarded,(_,products)=>{
                    Exception? cleanup=null;try{standard?.Complete();}catch(Exception e){cleanup=e;}
                    try{recipe.AfterCommitted?.Invoke(plan,products);}catch(Exception e){if(cleanup is not null)throw new AggregateException(cleanup,e);throw;}
                    if(cleanup is not null)throw cleanup;
                });
                if(result.Status==MachineTransactionStatus.RecoveryFailed&&Current())MachineFaultGuard.Record(machine,result);
                var batchResult=new MachineBatchResult(selected,null,result);
                automation.Complete(batchResult,Current);
                MachineAutomationApi.Dispatch(_machineId,MachineAutomationPhase.AfterBatch,automation);
                return batchResult;
            }
            return new(null,Unchanged()?"EZAPI_RECIPE_NO_MATCH: resources or power insufficient.":"EZAPI_RECIPE_STALE: inputs changed during selection.",null);
        }catch(Exception e){return new(selected,"EZAPI_RECIPE_CALLBACK: "+e.GetType().Name+": "+e.Message,null);}
        finally{automation?.Close();MachineRecipeApi.Exit();}
    }
    private static NativeInventorySnapshot[] Capture(MachineInventory s,GameRunIdentity run)
    {
        var slots=new List<GameInventory>{s.ItemInput,s.Output};if(s.Battery is {} b)slots.Add(b);if(s.Modules is {} m)slots.Add(m);if(s.Manual is {} manual)slots.Add(manual);slots.AddRange(s.LiquidInputs.Values);
        return slots.Select(i=>InventoryChecks.Read(i,run)).ToArray();
    }
    private static bool Same(NativeInventorySnapshot[] a,NativeInventorySnapshot[] b)=>a.Length==b.Length&&a.Select((x,i)=>InventoryChecks.Equal(x,b[i])).All(x=>x);
    private static GameItem? Single(GameSlotInventory? slot)
    {
        if(slot is null||slot.childItems.Count==0)return null;
        if(slot.childItems.Count!=1)throw new InvalidOperationException("EZAPI_RECIPE_SLOT: exactly one item required in resource slot.");
        return slot.childItems[0];
    }
    private sealed class Guarded:IMachineBatchTransaction
    {
        private readonly IMachineBatchTransaction _inner;private readonly Func<bool> _before,_after,_restored;
        internal Guarded(IMachineBatchTransaction inner,Func<bool> before,Func<bool> after,Func<bool> restored)=>(_inner,_before,_after,_restored)=(inner,before,after,restored);
        public bool Validate()=>_before()&&_inner.Validate()&&_before();
        public void Apply(IReadOnlyList<GameItem> products)=>_inner.Apply(products);
        public bool Verify()=>_inner.Verify()&&_after();
        public bool Restore()=>_inner.Restore()&&_restored();
        public void Fault(string reason)=>_inner.Fault(reason);
    }
}
