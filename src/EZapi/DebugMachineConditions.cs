using Il2Cpp;
namespace EZapi;

/// <summary>同一材料按钮集中验收真实原生准入与批次二次检查；所有图及规则均finally释放。</summary>
internal static class DebugMachineConditions
{
    private const string Id="ezapi.debug.condition_machine";
    internal static void Register()
    {
        var result=MachineRegistrationApi.RegisterMachine("ezapi.debug",new(Id,new(){Title="EZapi容器条件临时检查",ItemInput=new(3,3),
            Output=new(){Kind="container",Size=new(3,3),ContainerCondition=item=>item.identifier=="ezapi.sample.water_container"},
            LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Size=new(3,3),Condition=item=>item.identifier=="ezapi.sample.water_container"}},
            Battery=new(new(2,2))},new[]{new MachineRecipe("ezapi.debug.condition_machine_recipe",new[]{new MachineIngredient("ezapi.debug.material_stack",2)},
                new MachineContainerOutput(new[]{new MachineLiquidAmount("water",1)})){LiquidInputs=new[]{new MachineLiquidIngredient("water",1)}}}) {
                Power=new(7),Guide=null,EnableNightly=false,ItemOptions=new("EZapi容器条件检查机")
            });
        if(!result.Accepted)throw new InvalidOperationException(result.Reason);
    }
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"condition-allow","condition-output-entry","condition-input-entry","condition-output-stored","condition-input-stored",
            "condition-and","condition-throw","condition-dispose","condition-recipe"}) {
            var made=new List<GameItem>();var leases=new List<IDisposable>();
            var baseline=MachineContainerConditionApi.Snapshot().Count;
            try {
                var machine=ItemApi.Create(Id,true);made.Add(machine);
                check(MachineContainerConditionApi.Snapshot().Any(s=>s.OwnerId=="ezapi.debug"&&s.MachineId==Id&&!s.Faulted&&s.Slots.Count==2),mode+": unified registration installed both slot callbacks");
                if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("condition fixture layout absent");
                var material=ItemApi.Create("ezapi.debug.material_stack",true);made.Add(material);material.SetUnitCount(5);Put(slots.ItemInput,material,5);
                var source=ItemApi.Create("ezapi.sample.water_container",true);made.Add(source);Put(slots.LiquidInputs["water"],source,1);
                var target=ItemApi.Create("ezapi.sample.water_container",true);made.Add(target);WaterHelper.EmptyContainer(target);Put(slots.Output,target,1);
                var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery,1);
                var before=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());var sourceBefore=Liquids.CaptureProduction(source);var targetBefore=Liquids.CaptureProduction(target);
                using var bytes=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(machine.GetTagReadonly(MachineLayoutApi.LayoutTag).valueString));
                var parsed=JsonMachineLayout.Parse(bytes);var outputCalls=0;var inputCalls=0;
                Func<GameItem,bool> outputCondition=_=>{outputCalls++;if(mode=="condition-throw")throw new InvalidOperationException("deliberate condition fixture");return mode is not ("condition-output-entry" or "condition-output-stored");};
                Func<GameItem,bool> inputCondition=_=>{inputCalls++;return mode is not ("condition-input-entry" or "condition-input-stored");};
                var template=new MachineLayoutDefinition{Title=parsed.Title,Draggable=parsed.Draggable,ItemInput=parsed.ItemInput,Output=new(){Kind=parsed.Output.Kind,Size=parsed.Output.Size,ContainerCondition=outputCondition},
                    Battery=parsed.Battery,Modules=parsed.Modules,ManualSlot=parsed.ManualSlot,LiquidInputs=parsed.LiquidInputs.Select(s=>new MachineContainerSlot{SlotId=s.SlotId,Label=s.Label,Size=s.Size,AllowedIds=s.AllowedIds,AllowedTags=s.AllowedTags,Condition=inputCondition}).ToArray()};
                var lease=MachineContainerConditionApi.Register("ezapi.condition-test",machine.identifier,template);leases.Add(lease);
                check(MachineContainerConditionApi.Snapshot().Count==baseline+1,mode+": exact scoped rule registered");
                if(mode=="condition-and")leases.Add(MachineContainerConditionApi.Register("ezapi.condition-test-other",machine.identifier,new(){Title=parsed.Title,Draggable=parsed.Draggable,ItemInput=parsed.ItemInput,
                    Output=new(){Kind=parsed.Output.Kind,Size=parsed.Output.Size,ContainerCondition=_=>false},Battery=parsed.Battery,Modules=parsed.Modules,ManualSlot=parsed.ManualSlot,LiquidInputs=parsed.LiquidInputs}));
                if(mode=="condition-dispose"){lease.Dispose();check(MachineContainerConditionApi.Snapshot().Count==baseline,mode+": release removes own rule");}
                if(mode=="condition-output-entry") {
                    check(slots.Output.Expel(target),mode+": temporary output detached");
                    check(slots.Output.TryFindOneValidInventorySlot(target)?.IsValid()!=true,mode+": native output admission refuses actual vessel");
                }
                if(mode=="condition-input-entry") {
                    check(slots.LiquidInputs["water"].Expel(source),mode+": temporary input detached");
                    check(slots.LiquidInputs["water"].TryFindOneValidInventorySlot(source)?.IsValid()!=true,mode+": native input admission refuses actual vessel");
                }
                var liquidId=Liquid.Liquids[0].identifier;
                var recipe=new MachineRecipe("ezapi.debug."+mode.Replace('-','_'),new[]{new MachineIngredient(material.identifier,2)},
                    new MachineContainerOutput(new[]{new MachineLiquidAmount(liquidId,1)}){Condition=_=>mode!="condition-recipe"}) {
                    LiquidInputs=new[]{new MachineLiquidIngredient("water",1)}
                };
                var result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7)).Process(machine);
                var committed=mode is "condition-allow" or "condition-dispose";
                details.Add(new{mode,result,outputCalls,inputCalls,rules=MachineContainerConditionApi.Snapshot()});
                check(result.Committed==committed,mode+": expected processing result");
                check(Power.GetEnergy(battery)==(committed?93:100),mode+": power only consumed on commit");
                check(material.unitCount==(committed?3:5)&&material.parentInventory?.Pointer==slots.ItemInput.Pointer,mode+": native materials conserved");
                check(Liquids.CaptureProduction(source).TotalParts==sourceBefore.TotalParts-(committed?1000:0),mode+": source liquid conserved");
                check(Liquids.CaptureProduction(target).TotalParts==targetBefore.TotalParts+(committed?1000:0),mode+": target liquid conserved");
                if(!committed)check(InventoryChecks.Equal(before,InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun()))&&result.Transaction is null,mode+": rejected before resource transaction");
                if(mode=="condition-throw") {
                    var count=outputCalls;check(MachineContainerConditionApi.Snapshot().Single(s=>s.OwnerId=="ezapi.condition-test").Faulted,mode+": throwing rule faulted");
                    check(!MachineLayoutApi.Allows(target,slots.Output)&&outputCalls==count,mode+": faulted callback not repeatedly invoked");
                }
                check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(source)&&!InventoryFreezeApi.IsFrozen(target),mode+": no freeze leak");
            }catch(Exception error){check(false,mode+": "+error);MelonLoader.MelonLogger.Error("[EZapi] Container condition fixture: "+error);}
            finally {
                foreach(var lease in leases.AsEnumerable().Reverse())lease.Dispose();
                check(MachineContainerConditionApi.Snapshot().Count==baseline,mode+": rules released after test");
                foreach(var item in made.AsEnumerable().Reverse())try{if(item.Pointer!=IntPtr.Zero){item.parentInventory?.Expel(item);item.Destroy();}}catch(Exception error){check(false,mode+": cleanup "+error.Message);}
            }
        }
    }
    private static void Put(GameInventory inventory,GameItem item,int count)
    {
        var marker=inventory.TryFindOneValidInventorySlot(item);
        if(marker?.IsValid()!=true||marker.TryAcceptOnce(count)!=count)throw new InvalidOperationException("condition fixture insertion failed");
    }
}
