using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>合并检查配方选取、原生批次提交和故障补偿。仅临时图，输入Expel暂存，不代表通用堆叠消费。</summary>
internal static class DebugMachineRecipes
{
    internal static object? LastReport {get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            foreach(var mode in new[]{"commit","dynamic","missing","condition","water","power","full","partial","duplicate-liquid","reentrant","notify","admission","overlap","tag"}) {
                var made=new List<GameItem>();var executorDestroyed=new HashSet<IntPtr>();var products=new List<GameItem>();
                IDisposable? admission=null;var catalog=false;
                try {
                    var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);made.Add(machine);MachineLayoutApi.TryGetInventory(machine,out var slots);
                    var input=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(input);Put(slots.ItemInput,input);
                    if(mode=="overlap"){var extra=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(extra);Put(slots.ItemInput,extra);}
                    if(mode=="tag"){input.state.GetTag("EZAPI_RECIPE_PROBE").Enable();input.SyncModifiedState();}
                    var water=ItemApi.Create("ezapi.sample.water_container",true);made.Add(water);WaterHelper.FillWithBaseWater(water);Put(slots.LiquidInputs["water"],water);
                    var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery);
                    if(mode=="power")PowerHelper.SetPowerSourceAt(battery,0);
                    if(mode=="full") {
                        var grid=slots.Output.TryCast<GameGridInventory>()!;var fill=ItemDirectory.CreateEmptyItem("ezapi.debug.recipe_blocker");made.Add(fill);
                        fill.SetShape(new GridShapeBuilder(grid.inventoryShape.globalWidth,grid.inventoryShape.globalHeight).SetDataFill(1).Build());GeneralHelper.SetItemOwned(fill,true);Put(grid,fill);
                    }
                    var beforeWater=Liquids.CaptureProduction(water);var beforeEnergy=Power.GetEnergy(battery);var transactionCalls=0;var notes=0;Probe? transaction=null;MachineBatchResult? nested=null;
                    MachineBatchProcessor? processor=null;
                    var recipe=new MachineRecipe("ezapi.debug.recipe_probe",new[]{new MachineIngredient(mode=="missing"?"ezapi.debug.missing":"ezapi.debug.factory_item",1,_=>mode!="condition")},
                        new("ezapi.debug.factory_item",ResolveCount:mode=="dynamic"?_=>2:null,PrepareItem:(_,item)=>{products.Add(item);made.Add(item);})) {
                        LiquidInputs=mode=="duplicate-liquid"?new[]{new MachineLiquidIngredient("water",300),new MachineLiquidIngredient("water",300)}:new[]{new MachineLiquidIngredient("water",mode=="water"?501:1)},
                        Transaction=plan=>{transactionCalls++;if(mode=="reentrant")nested=processor!.Process(machine);return transaction=new Probe(plan,input,water,battery,mode=="partial");},
                        AfterCommitted=(_,_)=>{notes++;if(mode=="notify")throw new InvalidOperationException("expected recipe notification exception");}
                    };
                    if(mode=="overlap")recipe=recipe with {ItemInputs=new[]{new MachineIngredient("ezapi.debug.factory_item"),new MachineIngredient("ezapi.debug.factory_item",1,i=>i.uniqueId==input.uniqueId)}};
                    if(mode=="tag")recipe=recipe with {ItemInputs=new[]{new MachineIngredient(null){ItemTag="EZAPI_RECIPE_PROBE"}}};
                    if(mode=="admission") {
                        var unrelated=ItemDirectory.CreateEmptyItem("ezapi.debug.recipe_unrelated");made.Add(unrelated);unrelated.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());GeneralHelper.SetItemOwned(unrelated,true);
                        var grid=slots.ItemInput.TryCast<GameGridInventory>()!;
                        Check(grid.MayHaveValidInventorySlot(unrelated),"admission: native candidate allowed before recipe filter");
                        var submission=MachineRecipeApi.RegisterRecipes("ezapi.debug",machine.identifier,new[]{recipe},new(7));
                        if(!submission.Accepted)throw new InvalidOperationException(submission.Reason);catalog=true;
                        admission=MachineRecipeApi.InstallInputAdmission("ezapi.debug",machine.identifier);
                        Check(!grid.MayHaveValidInventorySlot(unrelated),"admission: unrelated input rejected by installed native guard");
                        MachineRecipeApi.RegisterRecipes("ezapi.debug",machine.identifier,Array.Empty<MachineRecipe>());
                        Check(grid.MayHaveValidInventorySlot(unrelated),"admission: removed catalog releases input restriction");
                        MachineRecipeApi.RegisterRecipes("ezapi.debug",machine.identifier,new[]{recipe},new(7));
                    }
                    processor=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7,PerOutput:mode=="dynamic"));
                    var result=mode=="admission"?MachineRecipeApi.ProcessBatch(machine):processor.Process(machine);var success=mode is "commit" or "dynamic" or "reentrant" or "notify" or "admission" or "overlap" or "tag";var attempted=success||mode=="partial";
                    if(result.Transaction?.Status is MachineTransactionStatus.Rejected or MachineTransactionStatus.Restored)foreach(var p in products)executorDestroyed.Add(p.Pointer);
                    details.Add(new{mode,result,transactionCalls,applies=transaction?.Applies??0,restores=transaction?.Restores??0,energyBefore=beforeEnergy,energyAfter=Power.GetEnergy(battery),waterBefore=beforeWater.TotalParts,waterAfter=Liquids.CaptureProduction(water).TotalParts,notifications=notes,nested});
                    Check(result.Committed==success,mode+": expected committed state");
                    Check((transaction?.Applies??0)==(attempted?1:0),mode+": no Apply on rejected selection or full output");
                    Check(Power.GetEnergy(battery)==beforeEnergy-(success?(mode=="dynamic"?14:7):0),mode+": actual power delta");
                    Check(Liquids.CaptureProduction(water).TotalParts==beforeWater.TotalParts-(success?1000:0),mode+": actual liquid delta");
                    Check(success?input.parentInventory is null:input.parentInventory?.Pointer==slots.ItemInput.Pointer,mode+": input graph unchanged or staged");
                    Check(products.Count==(success?(mode=="dynamic"?2:1):mode is "partial" or "full"?1:0),mode+": only selected executable recipe creates products");
                    if(mode=="full")Check(result.Transaction?.Status==MachineTransactionStatus.Rejected&&transaction?.Applies==0,"full: output planned before resource write");
                    if(mode=="partial")Check(result.Transaction?.Status==MachineTransactionStatus.Restored&&transaction?.Restores==1,"partial: one resource compensation");
                    if(mode=="reentrant")Check(nested?.Error=="EZAPI_RECIPE_REENTRANT_OR_STALE","reentrant: nested processor rejected");
                    Check(notes==(success?1:0)&&(mode!="notify"||result.Transaction?.CompletionError is not null),mode+": only verified commit emits one notification");
                    Check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(water),mode+": no leaked freeze");
                }finally {
                    admission?.Dispose();if(catalog)MachineRecipeApi.RegisterRecipes("ezapi.debug","ezapi.debug.machine_layout_probe",Array.Empty<MachineRecipe>());
                    foreach(var item in made.AsEnumerable().Reverse()) {
                        if(executorDestroyed.Contains(item.Pointer))continue;
                        if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("recipe fixture expel");item.Destroy();
                    }
                }
            }
            DebugRecipeReadOnly.Run(Check,details);
        }catch(Exception e){failed++;checks.Add(new{name="execution",passed=false,error=e.ToString()});MelonLogger.Error("[EZapi] Recipe probe: "+e);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="temporary recipe selection and custom transaction; no generic material consumption, player delivery or saving"};
        EZapiBootstrap.WriteDiagnostics();return $"配方批次检查：{passed}通过，{failed}失败，已导出。";
    }
    private static void Put(GameInventory inventory,GameItem item)
    {var marker=inventory.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("recipe fixture admission: "+inventory.identifier);}
    private sealed class Probe:IMachineBatchTransaction
    {
        private readonly MachineRecipePlan _plan;private readonly GameItem _input,_water,_battery;private readonly bool _partial;private readonly GridShape _shape;
        private readonly NativeTagBackup _backup;private readonly MachineLiquidSnapshot _before;private readonly int _energy;private readonly Dictionary<GameItem,GridShape> _inputs;
        private IReadOnlyList<GameItem> _products=Array.Empty<GameItem>();internal int Applies,Restores;
        internal Probe(MachineRecipePlan plan,GameItem input,GameItem water,GameItem battery,bool partial)
        {_plan=plan;_input=input;_water=water;_battery=battery;_partial=partial;_shape=input.modifiedShape.Clone();_backup=NativeTagBackup.Capture(water.state);_before=Liquids.CaptureProduction(water);_energy=Power.GetEnergy(battery);_inputs=plan.Context.Items.ToDictionary(t=>t.Item,t=>t.Item.modifiedShape.Clone());}
        public bool Validate()=>_inputs.Keys.All(i=>i.unitCount==1&&i.parentInventory?.Pointer==_plan.Inventory.ItemInput.Pointer);
        public void Apply(IReadOnlyList<GameItem> products) {
            Applies++;_products=products;
            foreach(var item in _inputs.Keys)if(!_plan.Inventory.ItemInput.Expel(item))throw new InvalidOperationException("recipe input expel");
            WaterHelper.Remove(_water,1000);if(!PowerHelper.DrawPowerSource(_battery,_plan.PowerCost))throw new InvalidOperationException("recipe power");
            if(_partial)throw new InvalidOperationException("expected recipe partial failure");
            foreach(var p in _plan.OutputPlacements) {
                var marker=_plan.Inventory.Output.TryInventorySlot(p.Item,1,p.Shape,null);
                if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("recipe output delivery");
            }
        }
        public bool Verify()=>_inputs.Keys.All(i=>i.parentInventory is null)&&_products.All(p=>p.parentInventory?.Pointer==_plan.Inventory.Output.Pointer);
        public bool Restore() {
            Restores++;var ok=true;void Try(Action a){try{a();}catch{ok=false;}}
            foreach(var p in _products)Try(()=>{if(p.parentInventory is {} parent&&!parent.Expel(p))throw new InvalidOperationException("product expel");});
            Try(()=>{_backup.Restore(_water.state);_water.SyncModifiedState();if(!LiquidMetadata.Same(_before,Liquids.CaptureProduction(_water)))throw new InvalidOperationException("water restore");});
            Try(()=>{PowerHelper.SetPowerSourceAt(_battery,_energy);if(Power.GetEnergy(_battery)!=_energy)throw new InvalidOperationException("power restore");});
            foreach(var pair in _inputs)Try(()=>{if(pair.Key.parentInventory is null) {var marker=_plan.Inventory.ItemInput.TryInventorySlot(pair.Key,1,pair.Value,null);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("input restore");}});
            return ok&&_inputs.Keys.All(i=>i.parentInventory?.Pointer==_plan.Inventory.ItemInput.Pointer);
        }
        public void Fault(string reason)=>MelonLogger.Warning("[EZapi] Recipe fixture recovery: "+reason);
    }
}
