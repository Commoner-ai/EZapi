using Il2Cpp;
namespace EZapi;

/// <summary>真实配方入口的只读条件/解析器/工厂/Prepare回归；仅临时机器图，不保存或过夜。</summary>
internal static class DebugRecipeReadOnly
{
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"readonly","machine-condition","false-condition","item-condition","false-item-condition","liquid-condition","false-liquid-condition","liquid-amount","output-count","output-id","power-cost","liquid-output-amount","liquid-output-value","liquid-output-quality","transaction-factory","transaction-validate","prepare-input-tag","prepare-machine-tag","prepare-battery-tag","prepare-water-tag","prepare-output-tag","container-condition","container-contents"}) {
            GameItem? machine=null;var loose=new List<GameItem>();
            try {
                var containerMode=mode.StartsWith("container-",StringComparison.Ordinal);
                machine=ItemApi.Create(containerMode?"ezapi.debug.factory_item":"ezapi.debug.machine_layout_probe",true);
                if(containerMode){machine.identifier="ezapi.debug.readonly_container";MachineLayoutApi.Create(machine,new(){ItemInput=new(3,3),Output=new(){Kind="container",Size=new(2,2)},Battery=new(new(2,2),false),LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Size=new(2,2)}}});}
                MachineLayoutApi.TryGetInventory(machine,out var slots);
                var input=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(input);Put(slots.ItemInput,input);loose.Remove(input);
                var water=ItemApi.Create("ezapi.sample.water_container",true);loose.Add(water);WaterHelper.FillWithBaseWater(water);Put(slots.LiquidInputs["water"],water);loose.Remove(water);
                var battery=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery);loose.Remove(battery);
                GameItem? oldOutput=null;
                if(mode=="prepare-output-tag"){oldOutput=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(oldOutput);Put(slots.Output,oldOutput);loose.Remove(oldOutput);}
                if(containerMode){oldOutput=ItemApi.Create("ezapi.sample.water_container",true);loose.Add(oldOutput);WaterHelper.EmptyContainer(oldOutput);Put(slots.Output,oldOutput);loose.Remove(oldOutput);}
                var calls=0;var applies=0;GameItem? mutated=null;
                void Mutate(GameItem target){calls++;mutated=target;target.state.GetTag("EZAPI_DEBUG_RECIPE_READONLY").Enable().SetString(mode);target.SyncModifiedState();}
                var recipe=new MachineRecipe("ezapi.debug.readonly_recipe",new[]{new MachineIngredient(input.identifier)},new("ezapi.debug.factory_item"));
                var power=new MachinePowerRule(0);var supplied=slots;
                switch(mode) {
                    case "machine-condition":case "false-condition":recipe=recipe with{MachineCondition=_=>{Mutate(machine);return mode!="false-condition";}};break;
                    case "item-condition":case "false-item-condition":recipe=recipe with{ItemInputs=new[]{new MachineIngredient(input.identifier,Condition:_=>{Mutate(input);return mode!="false-item-condition";})}};break;
                    case "liquid-condition":case "false-liquid-condition":recipe=recipe with{LiquidInputs=new[]{new MachineLiquidIngredient("water",1,Condition:_=>{Mutate(water);return mode!="false-liquid-condition";})}};break;
                    case "liquid-amount":recipe=recipe with{LiquidInputs=new[]{new MachineLiquidIngredient("water",1,ResolveMillilitres:_=>{Mutate(input);return 1;})}};break;
                    case "output-count":recipe=recipe with{Output=recipe.Output with{ResolveCount=_=>{Mutate(input);return 1;}}};break;
                    case "output-id":recipe=recipe with{Output=recipe.Output with{ResolveItemId=_=>{Mutate(input);return input.identifier;}}};break;
                    case "power-cost":power=new(0,ResolveCost:_=>{Mutate(battery);return 0;});break;
                    case "liquid-output-amount":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1,ResolveMillilitres:_=>{Mutate(input);return 1;})}}};break;
                    case "liquid-output-value":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1,ResolveValue:_=>{Mutate(input);return 1;})}}};break;
                    case "liquid-output-quality":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1){ResolveQualityBasis=_=>{Mutate(input);return 1;}}}}};break;
                    case "transaction-factory":recipe=recipe with{Transaction=_=>{Mutate(input);return new RefusedTransaction(()=>{},()=>applies++);}};break;
                    case "transaction-validate":recipe=recipe with{Transaction=_=>new RefusedTransaction(()=>Mutate(input),()=>applies++)};break;
                    case "prepare-input-tag":case "prepare-machine-tag":case "prepare-battery-tag":case "prepare-water-tag":case "prepare-output-tag":
                        recipe=recipe with{Output=recipe.Output with{PrepareItem=(_,_)=>Mutate(mode=="prepare-machine-tag"?machine:mode=="prepare-battery-tag"?battery:mode=="prepare-water-tag"?water:mode=="prepare-output-tag"?oldOutput!:input)}};break;
                    case "container-condition":case "container-contents":
                        recipe=recipe with{Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("water",1)}){
                            Condition=mode=="container-condition"?_=>{Mutate(oldOutput!);return false;}:null,
                            ResolveContents=mode=="container-contents"?_=>{Mutate(input);return new[]{new MachineLiquidPart("water",1000)};}:null}};break;
                }
                var beforeWater=Liquids.CaptureProduction(water);var beforeOutput=slots.Output.childItems.Count;
                var result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},power).Process(machine,supplied);
                var success=mode=="readonly";
                check(result.Committed==success,"readonly-recipe-"+mode+": expected commit or rejection");
                check(slots.ItemInput.childItems.Count==(success?0:1)&&slots.Output.childItems.Count==beforeOutput+(success?1:0),"readonly-recipe-"+mode+": exact native input/output delta");
                check(Power.GetEnergy(battery)==100&&LiquidMetadata.Same(beforeWater,Liquids.CaptureProduction(water)),"readonly-recipe-"+mode+": no energy/liquid debit");
                check(success||calls>0&&applies==0&&(result.Error?.StartsWith("EZAPI_RECIPE_STALE",StringComparison.Ordinal)==true||result.Transaction?.Status==MachineTransactionStatus.Rejected),"readonly-recipe-"+mode+": callback reached and write phase refused");
                check(success||mutated?.GetTagReadonly("EZAPI_DEBUG_RECIPE_READONLY")?.valueString==mode,"readonly-recipe-"+mode+": audit does not pretend to undo illegal author writes");
                check(!InventoryFreezeApi.IsFrozen(machine),"readonly-recipe-"+mode+": freeze released");
                details.Add(new{mode="readonly-recipe-"+mode,result,calls,applies,scope="real synchronous processor on temporary native machine; no save/night"});
            }finally {
                foreach(var item in loose.AsEnumerable().Reverse())if(item.parentInventory is null)item.Destroy();
                if(machine is not null)foreach(var item in SaveGraphApi.ItemsForObservation(machine).Reverse()){
                    if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("readonly recipe cleanup expel");item.Destroy();
                }
            }
        }
    }
    private static void Put(GameInventory inventory,GameItem item)
    {var marker=inventory.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("readonly recipe fixture admission");}
    private sealed class RefusedTransaction:IMachineBatchTransaction
    {
        private readonly Action _validate,_apply;
        internal RefusedTransaction(Action validate,Action apply)=>(_validate,_apply)=(validate,apply);
        public bool Validate(){_validate();return true;}
        public void Apply(IReadOnlyList<GameItem> products){_apply();throw new InvalidOperationException("illegal write phase reached");}
        public bool Verify()=>false;public bool Restore()=>false;
        public void Fault(string reason)=>throw new InvalidOperationException("readonly recipe unexpectedly entered recovery: "+reason);
    }
}
