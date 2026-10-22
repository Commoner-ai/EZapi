using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>
/// 标准配方的同步资源日记。Apply内冻结完整机器图，原生接纳保管材料；确认提交后才消费。
/// 无跨帧/崩溃恢复或自动保存；自定义Transaction仍完全由作者负责。
/// </summary>
internal sealed class DefaultMachineTransaction:IMachineBatchTransaction,IDisposable
{
    private sealed class Resource
    {
        internal readonly GameItem Item;internal readonly int Instance;internal readonly IntPtr State,Dict,Pointer,Parent;
        internal readonly NativeTagBackup Tags;internal readonly long Value;
        internal Resource(GameItem item){Item=item;Instance=item.uniqueId;Pointer=item.Pointer;Parent=item.parentInventory?.Pointer??IntPtr.Zero;State=item.state.Pointer;Dict=item.state.dict.Pointer;Tags=NativeTagBackup.Capture(item.state);Value=item.unitValue;}
        internal bool Current()=>Item.Pointer==Pointer&&Item.uniqueId==Instance&&(Item.parentInventory?.Pointer??IntPtr.Zero)==Parent&&Item.state.Pointer==State&&Item.state.dict.Pointer==Dict;
        internal void Restore(){if(!Current())throw Error("resource identity changed");Tags.Restore(Item.state);Item.SyncModifiedState();Item.unitValue=Value;}
    }
    private readonly MachineRecipePlan _plan;private readonly GameRunIdentity _run;private readonly MachineMaterialEscrow _materials;
    private readonly Resource[] _resources;private readonly Dictionary<string,MachineLiquidSnapshot> _before,_after;
    private readonly ExternalPowerApi.PowerJournal? _external;
    private readonly GameItem? _battery;private readonly int _energy;private IReadOnlyList<GameItem> _products=Array.Empty<GameItem>();
    internal DefaultMachineTransaction(MachineRecipePlan plan)
    {
        _plan=plan;_run=NativeAccess.RequireRun();_materials=new(plan.Context,plan.Recipe.MaterialContents);
        _before=plan.Context.LiquidContainers.ToDictionary(p=>p.Key,p=>Liquids.CaptureProduction(p.Value),StringComparer.Ordinal);
        _after=new(_before,StringComparer.Ordinal);foreach(var take in plan.Context.Liquids)_after[take.SlotId]=take.After;
        // 必需签名在创建产物前核对；指定成分只改对应原生标签并复用原生倒出后处理。
        if(plan.Recipe.LiquidInputs.Any(i=>i.LiquidId is not null))MachineLiquidDebit.RequireAvailable();
        if(plan.OutputContainer is not null)MachineLiquidFill.RequireAvailable();
        _battery=plan.Inventory.Battery?.childItems.Count==1?plan.Inventory.Battery.childItems[0]:null;
        _energy=_battery is null?0:Power.GetEnergy(_battery);
        _resources=plan.Context.LiquidContainers.Values.Concat(plan.OutputContainer is {} target?new[]{target}:Array.Empty<GameItem>())
            .Concat(_battery is null||ExternalPowerApi.IsConnector(_battery)?Array.Empty<GameItem>():new[]{_battery}).DistinctBy(i=>i.Pointer).Select(i=>new Resource(i)).ToArray();
        if(_battery is not null&&ExternalPowerApi.IsConnector(_battery))_external=ExternalPowerApi.Open(plan.Context.Machine,_battery,plan.PowerCost);
    }
    public bool Validate()=>NativeAccess.SameRun(_run)&&_materials.Validate()&&_resources.All(r=>r.Current())&&
        _before.All(p=>LiquidMetadata.Same(p.Value,Liquids.CaptureProduction(_plan.Context.LiquidContainers[p.Key])))&&
        (_plan.OutputContainer is null||LiquidMetadata.Same(_plan.OutputContainerBefore!,Liquids.CaptureProduction(_plan.OutputContainer)))&&
        (_battery is null?_plan.PowerCost==0:Power.GetEnergy(_battery)==_energy&&(_external?.Validate()??PowerHelper.CanDrawPowerSource(_battery,_plan.PowerCost)));
    internal void Prepare(GameItem product)
    {
        if(_plan.OutputContents.Count==0)return;
        var physical=Liquids.Capture(product);
        var desired=_plan.OutputContents.ToDictionary(p=>p.LiquidId,StringComparer.Ordinal);
        if(desired.Keys.Except(physical.Contents.Select(p=>p.Id),StringComparer.Ordinal).Any())throw Error("unknown output liquid ID");
        var expected=new MachineLiquidSnapshot(physical.CapacityParts,physical.Contents.Select(p=>desired.TryGetValue(p.Id,out var part)?part:new MachineLiquidPart(p.Id,0)).ToArray());
        WaterHelper.EmptyContainer(product);
        foreach(var p in expected.Contents)if(p.Parts>0)WaterHelper.AddLiquid(product,p.LiquidId,p.Parts);
        LiquidMetadata.Store(product,expected);
    }
    public void Apply(IReadOnlyList<GameItem> products)
    {
        _products=products;
        if(_plan.OutputPlacements.Count!=products.Count)throw Error("output placement plan incomplete");
        _materials.Begin(_plan.Context.Machine,products);_materials.Stage();
        for(var index=0;index<_plan.Context.Liquids.Count;index++) {
            var take=_plan.Context.Liquids[index];
            if(!LiquidMetadata.Same(take.Before,Liquids.CaptureProduction(take.Container)))throw Error("liquid changed before debit");
            if(_plan.Recipe.LiquidInputs[index].LiquidId is not null){MachineLiquidDebit.Apply(take);continue;}
            var amount=checked(take.Before.TotalParts-take.After.TotalParts);
            if(amount>0)WaterHelper.Remove(take.Container,amount);
            LiquidMetadata.Store(take.Container,take.After);
        }
        if(_external is not null)_external.Debit();
        else if(_plan.PowerCost>0&&(_battery is null||!PowerHelper.DrawPowerSource(_battery,_plan.PowerCost)))throw Error("native power debit refused");
        if(_plan.OutputContainer is not null)MachineLiquidFill.Apply(_plan);
        foreach(var placement in _plan.OutputPlacements)if(_materials.Deliver(placement.Item,_plan.Inventory.Output,placement.Shape)!=1)throw Error("native output delivery refused");
    }
    public bool Verify()=>NativeAccess.SameRun(_run)&&_materials.Verify()&&(_external?.Verify()??true)&&
        _after.All(p=>LiquidMetadata.Same(p.Value,Liquids.CaptureProduction(_plan.Context.LiquidContainers[p.Key])))&&
        (_plan.OutputContainer is null||LiquidMetadata.Same(_plan.OutputContainerAfter!,Liquids.CaptureProduction(_plan.OutputContainer)))&&
        (_battery is null||Power.GetEnergy(_battery)==_energy-_plan.PowerCost)&&
        _products.All(p=>p.parentInventory?.Pointer==_plan.Inventory.Output.Pointer);
    public bool Restore()
    {
        if(!NativeAccess.SameRun(_run)){Dispose();return false;}
        var ok=true;void Attempt(Action action){try{action();}catch{ok=false;}}
        foreach(var product in _products)Attempt(()=>{if(!_materials.ExpelProduct(product))throw Error("product expel failed");});
        if(_external is not null)Attempt(()=>{if(!_external.Restore())throw Error("external power restore failed");});
        foreach(var resource in _resources)Attempt(resource.Restore);
        try{if(!_materials.Restore())ok=false;}catch{ok=false;}finally{Dispose();}
        return ok&&_before.All(p=>LiquidMetadata.Same(p.Value,Liquids.CaptureProduction(_plan.Context.LiquidContainers[p.Key])))&&
            (_plan.OutputContainer is null||LiquidMetadata.Same(_plan.OutputContainerBefore!,Liquids.CaptureProduction(_plan.OutputContainer)))&&
            (_external is not null?_external.IsRestored:_battery is null||Power.GetEnergy(_battery)==_energy);
    }
    internal bool RestoredExternalPower=>_external?.IsRestored??false;
    internal void Complete()=>_materials.Consume();
    internal IReadOnlyList<int> DestroyedGraphInstanceIds=>_materials.DestroyedGraphInstanceIds;
    public void Dispose(){_materials.Abandon();_external?.Dispose();}
    public void Fault(string reason){_external?.Fault(reason);MelonLogger.Error("[EZapi] EZAPI_MACHINE_DEFAULT_RECOVERY: "+reason);}
    private static InvalidOperationException Error(string reason)=>new("EZAPI_MACHINE_DEFAULT: "+reason);
}
