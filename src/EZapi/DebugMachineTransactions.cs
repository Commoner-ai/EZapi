using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>只在按钮触发时操作临时目录实例。资源事务显式实现；不改玩家库存、不保存、不伪装为标准配方运行时。</summary>
internal static class DebugMachineTransactions
{
    internal static object? LastReport {get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive)return "请先放下拖拽物品。";
        if(!VisibleStoreInventory.IsReady)return "请先进入测试档店铺。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            foreach(var mode in new[]{"reject","commit","partial","verify","notify"}) {
                var made=new List<GameItem>();var prepared=new List<GameItem>();var executorCleaned=new HashSet<IntPtr>();
                try {
                    var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);made.Add(machine);
                    if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("layout unavailable");
                    var input=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(input);
                    var water=ItemApi.Create("ezapi.sample.water_container",true);made.Add(water);
                    var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);
                    WaterHelper.FillWithBaseWater(water);
                    PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);
                    static void Put(GameInventory inventory,GameItem item) {
                        var marker=inventory.TryFindOneValidInventorySlot(item);
                        if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1||item.parentInventory?.Pointer!=inventory.Pointer)
                            throw new InvalidOperationException("temporary fixture admission failed: "+inventory.identifier);
                    }
                    Put(slots.ItemInput,input);Put(slots.LiquidInputs["water"],water);Put(slots.Battery!,battery);
                    var before=Liquids.Capture(water);
                    Check(Power.GetEnergy(battery)==100&&before.TotalParts>=1000,mode+": native energy and liquid fixture ready");
                    var transaction=new ProbeTransaction(slots,input,water,battery,mode);
                    var context=new MachineBatchContext(machine,new[]{new MachineItemTake(input,1){Value=20}},new Dictionary<string,GameItem>{{"water",water}},1);
                    var notes=0;
                    var result=MachineTransactionApi.Run("ezapi.debug",context,new[]{new MachineProductDefinition("ezapi.debug.factory_item",Prepare:(_,product)=>{
                        made.Add(product);prepared.Add(product);transaction.Prepare(product);
                    })},transaction,(_,_)=>{notes++;if(mode=="notify")throw new InvalidOperationException("expected notification failure");});
                    if(result.Status is MachineTransactionStatus.Rejected or MachineTransactionStatus.Restored)
                        foreach(var p in prepared)executorCleaned.Add(p.Pointer);
                    details.Add(new{mode,result,transaction.Validations,transaction.Applies,transaction.Restores,transaction.Faults,
                        energyBefore=100,energyAfter=Power.GetEnergy(battery),liquidBefore=before.TotalParts,liquidAfter=Liquids.Capture(water).TotalParts,workNotifications=notes});
                    var success=mode is "commit" or "notify";
                    Check(result.Status==(success?MachineTransactionStatus.Committed:mode=="reject"?MachineTransactionStatus.Rejected:MachineTransactionStatus.Restored),mode+": expected transaction status");
                    Check(transaction.Applies==(mode=="reject"?0:1)&&transaction.Restores==(mode is "partial" or "verify"?1:0)&&transaction.Faults==0,mode+": exactly-once Apply and bounded compensation");
                    Check(Power.GetEnergy(battery)==(success?93:100),mode+": actual energy delta verified");
                    Check(Liquids.Capture(water).TotalParts==before.TotalParts-(success?1000:0),mode+": actual liquid delta verified");
                    Check(success?input.parentInventory is null:input.parentInventory?.Pointer==slots.ItemInput.Pointer,mode+": input ownership graph verified");
                    Check(slots.Output.childItems.Count==(success?1:0),mode+": exact product count in output");
                    Check(notes==(success?1:0)&&(mode!="notify"||result.CompletionError is not null)&&(!success||!result.MayRetry),mode+": work notification and retry semantics");
                    if(!success&&mode!="reject")Check(transaction.OutputsDetached,mode+": resource Restore expelled generated output before framework cleanup");
                }finally {
                    // 已提交产品归事务调用方，失败游离产品归执行器；不重复Destroy失败路径已清理的产品。
                    foreach(var item in made.AsEnumerable().Reverse()) {
                        if(item.Pointer==IntPtr.Zero||executorCleaned.Contains(item.Pointer))continue;
                        if(item.parentInventory is { } parent && !parent.Expel(item))throw new InvalidOperationException("temporary item expel failed during cleanup");
                        if(item.unitCount>0)item.Destroy();
                    }
                }
            }
            foreach(var afterTarget in new[]{false,true}) {
                var mode=afterTarget?"pour-target-failure":"pour-source-failure";
                var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);GameItem? source=null,target=null;
                try {
                    MachineLayoutApi.TryGetInventory(machine,out var slots);
                    source=ItemApi.Create("ezapi.sample.water_container",true);target=ItemApi.Create("ezapi.sample.water_container",true);
                    WaterHelper.FillWithBaseWater(source);WaterHelper.EmptyContainer(target);
                    foreach(var item in new[]{source,target}) {
                        var slot=slots.ItemInput.TryFindOneValidInventorySlot(item);
                        if(slot?.IsValid()!=true||slot.TryAcceptOnce(1)!=1)throw new InvalidOperationException("pour fixture admission");
                    }
                    var beforeA=Liquids.CaptureProduction(source);var beforeB=Liquids.CaptureProduction(target);
                    var valueA=source.unitValue;var valueB=target.unitValue;
                    var result=LiquidTransferApi.Pour(source,target,1000,()=>afterTarget?WaterHelper.GetTotalVolume(target)==0:WaterHelper.GetTotalVolume(source)==beforeA.TotalParts);
                    details.Add(new{mode,result});
                    Check(result.Status=="Restored"&&!result.Indeterminate,mode+": partial native pour restored");
                    Check(LiquidMetadata.Same(beforeA,Liquids.CaptureProduction(source)),mode+": source exact composition restored");
                    Check(LiquidMetadata.Same(beforeB,Liquids.CaptureProduction(target)),mode+": target exact composition restored");
                    Check(source.unitValue==valueA&&target.unitValue==valueB,mode+": original values restored");
                    Check(!InventoryFreezeApi.IsFrozen(source)&&!InventoryFreezeApi.IsFrozen(target),mode+": freeze leases released");
                }finally {
                    foreach(var item in new[]{target,source,machine})if(item is not null) {
                        if(item.parentInventory is { } parent&&!parent.Expel(item))throw new InvalidOperationException("pour cleanup expel");
                        item.Destroy();
                    }
                }
            }
        }catch(Exception e){failed++;checks.Add(new{name="execution",passed=false,error=e.ToString()});MelonLogger.Error("[EZapi] Machine transaction probe: "+e);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,
            scope="temporary custom resource transactions with real item expel/admission, native water Remove and PowerHelper debit, plus frozen bounded-pour rollback; no standard recipe selection, scheduler, player delivery or save"};
        EZapiBootstrap.WriteDiagnostics();return $"加工事务检查：{passed}通过，{failed}失败，已导出。";
    }

    private sealed class ProbeTransaction:IMachineBatchTransaction
    {
        private readonly MachineInventory _slots;private readonly GameItem _input,_water,_battery;
        private readonly GridShape _shape;private readonly NativeTagBackup _waterBackup;private readonly LiquidSnapshot _before;private readonly string _mode;
        private IReadOnlyList<InventoryPlacement> _plan=Array.Empty<InventoryPlacement>();
        internal IReadOnlyList<GameItem> Products=Array.Empty<GameItem>();
        internal bool OutputsDetached;
        internal int Validations,Applies,Restores,Faults;
        internal ProbeTransaction(MachineInventory slots,GameItem input,GameItem water,GameItem battery,string mode)
        { _slots=slots;_input=input;_water=water;_battery=battery;_mode=mode;_shape=input.modifiedShape.Clone();_waterBackup=NativeTagBackup.Capture(water.state);_before=Liquids.Capture(water); }
        internal void Prepare(GameItem product)=>_plan=InventoryPlacementApi.PlanWholeGrid(_slots.Output.TryCast<GameGridInventory>()!,new[]{product});
        public bool Validate() {
            Validations++;
            return _mode!="reject"&&_input.parentInventory?.Pointer==_slots.ItemInput.Pointer&&_slots.Output.childItems.Count==0&&
                _water.parentInventory?.Pointer==_slots.LiquidInputs["water"].Pointer&&_battery.parentInventory?.Pointer==_slots.Battery?.Pointer&&
                _input.unitCount==1&&Power.GetEnergy(_battery)==100&&Liquids.Capture(_water).TotalParts==_before.TotalParts&&
                PowerHelper.CanDrawPowerSource(_battery,7)&&!_slots.ItemInput.IsRemoveLocked()&&!_slots.Output.IsInsertLocked();
        }
        public void Apply(IReadOnlyList<GameItem> products) {
            Applies++;Products=products;
            if(!_slots.ItemInput.Expel(_input))throw new InvalidOperationException("input expel failed");
            WaterHelper.Remove(_water,1000);
            if(!PowerHelper.DrawPowerSource(_battery,7))throw new InvalidOperationException("native energy debit failed");
            if(_mode=="partial")throw new InvalidOperationException("expected partial-write failure");
            foreach(var p in _plan) {
                var marker=_slots.Output.TryInventorySlot(p.Item,1,p.Shape,null);
                if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("product delivery failed");
            }
        }
        public bool Verify()=>_mode!="verify"&&_input.parentInventory is null&&Power.GetEnergy(_battery)==93&&
            Liquids.Capture(_water).TotalParts==_before.TotalParts-1000&&_slots.Output.childItems.Count==Products.Count&&
            Products.All(i=>i.parentInventory?.Pointer==_slots.Output.Pointer);
        public bool Restore() {
            Restores++;var okay=true;
            void Try(Action restore){try{restore();}catch{okay=false;}}
            foreach(var product in Products)Try(()=>{if(product.parentInventory is { } parent&&!parent.Expel(product))throw new InvalidOperationException("output expel");});
            OutputsDetached=Products.All(p=>p.parentInventory is null);
            Try(()=>{
                _waterBackup.Restore(_water.state);_water.SyncModifiedState();
                var after=Liquids.Capture(_water);if(after.TotalParts!=_before.TotalParts||!after.Contents.SequenceEqual(_before.Contents))throw new InvalidOperationException("liquid restore readback");
            });
            Try(()=>{PowerHelper.SetPowerSourceAt(_battery,100);if(Power.GetEnergy(_battery)!=100)throw new InvalidOperationException("energy restore readback");});
            Try(()=>{
                if(_input.parentInventory is null) {
                    var marker=_slots.ItemInput.TryInventorySlot(_input,1,_shape,null);
                    if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("input restore");
                }
                if(_input.parentInventory?.Pointer!=_slots.ItemInput.Pointer||InventoryChecks.Shape(_input.modifiedShape)!=InventoryChecks.Shape(_shape))throw new InvalidOperationException("input restore readback");
            });
            return okay;
        }
        public void Fault(string reason)=>Faults++;
    }
}
