using Il2Cpp;
namespace EZapi;

/// <summary>随“材料保管检查”集中运行的临时原生夹具：只消费指定成分及失败后的完整资源补偿。</summary>
internal static class DebugComponentLiquids
{
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"component-mixture","component-repeat","component-then-mixture","component-empty","component-rollback","component-partial-output"}) {
            var made=new List<GameItem>();var products=new List<GameItem>();var destroyed=new HashSet<IntPtr>();IDisposable? admission=null;
            try {
                var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);made.Add(machine);
                if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("native machine layout missing");
                var material=ItemApi.Create("ezapi.debug.material_stack",true);made.Add(material);material.SetUnitCount(5);Put(slots.ItemInput,material,5);
                var liquid=ItemApi.Create("ezapi.sample.water_container",true);made.Add(liquid);Put(slots.LiquidInputs["water"],liquid,1);
                WaterHelper.EmptyContainer(liquid);
                var catalog=Liquid.Liquids.ToArray();
                if(catalog.Length<2)throw new InvalidOperationException("two native liquid components required");
                var selected=catalog[0].identifier;var other=catalog[1].identifier;
                WaterHelper.AddLiquid(liquid,selected,mode=="component-empty"?1000:2000);
                if(mode!="component-empty")WaterHelper.AddLiquid(liquid,other,1500);
                var physical=MachineLiquidSnapshot.From(Liquids.Capture(liquid));
                var annotated=new MachineLiquidSnapshot(physical.CapacityParts,physical.Contents.Select(p=>p with {Value=p.Parts/100m,QualityBasis=p.Parts/200m}).ToArray());
                if(!Liquids.SetProductionMetadata(liquid,annotated).Committed)throw new InvalidOperationException("native fixture metadata not committed");
                var before=Liquids.CaptureProduction(liquid);var tags=NativeTagBackup.Capture(liquid.state);
                var state=liquid.state.Pointer;var dict=liquid.state.dict.Pointer;var pointer=liquid.Pointer;var instance=liquid.uniqueId;
                var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery,1);
                var inventoryBefore=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());
                var ingredients=new List<MachineLiquidIngredient>{new("water",1,selected)};
                if(mode=="component-repeat")ingredients.Add(new("water",1,selected));
                if(mode=="component-then-mixture")ingredients.Add(new("water",1));
                var expected=before;
                foreach(var spec in ingredients)expected=LiquidCompositionMath.Consume(expected,spec.Millilitres*1000,spec.LiquidId);
                var reject=mode is "component-rollback" or "component-partial-output";
                admission=InventoryAdmissionApi.Register("ezapi.debug",machine.identifier,slots.Output.identifier,(_,grid)=>
                    !reject||material.unitCount==5||mode=="component-partial-output"&&grid.childItems.Count==0);
                var recipe=new MachineRecipe("ezapi.debug."+mode.Replace('-','_'),new[]{new MachineIngredient(material.identifier,2)},
                    new MachineItemOutput("ezapi.debug.factory_item",mode=="component-partial-output"?2:1,PrepareItem:(_,p)=>{products.Add(p);made.Add(p);})) {
                    LiquidInputs=ingredients.AsReadOnly()
                };
                var result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7)).Process(machine);
                if(result.Transaction?.Status is MachineTransactionStatus.Restored or MachineTransactionStatus.Rejected)
                    foreach(var product in products)destroyed.Add(product.Pointer);
                var actual=Liquids.CaptureProduction(liquid);
                details.Add(new{mode,result,before,expected,actual});
                check(result.Committed==!reject,mode+": expected commit outcome");
                check(liquid.Pointer==pointer&&liquid.uniqueId==instance&&liquid.state.Pointer==state&&liquid.state.dict.Pointer==dict&&liquid.parentInventory?.Pointer==slots.LiquidInputs["water"].Pointer,
                    mode+": container identity/state/dictionary/parent preserved");
                check(LiquidMetadata.Same(reject?before:expected,actual),mode+": all component quantities/value/quality exact");
                check(Power.GetEnergy(battery)==(reject?100:93),mode+": exact power delta or restoration");
                check(reject?InventoryChecks.Equal(inventoryBefore,InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun())):material.unitCount==3,
                    mode+": material conservation or exact inventory restoration");
                check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(liquid),mode+": no leaked graph freeze");
                if(reject) {
                    check(result.Transaction?.Status==MachineTransactionStatus.Restored,mode+": default transaction compensated native delivery failure");
                    check(tags.Matches(liquid.state),mode+": complete original native tag values restored");
                    check(slots.Output.childItems.Count==0,mode+": no partially delivered output remains");
                } else {
                    check(actual.Contents.Single(p=>p.LiquidId==other).Parts==expected.Contents.Single(p=>p.LiquidId==other).Parts,
                        mode+": unselected component follows only explicitly requested mixture debit");
                }
            }catch(Exception error){check(false,mode+": "+error);MelonLoader.MelonLogger.Error("[EZapi] Component liquid fixture: "+error);}
            finally {
                admission?.Dispose();
                foreach(var item in made.AsEnumerable().Reverse())if(!destroyed.Contains(item.Pointer))try {
                    if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("fixture expel failed");
                    if(item.parentInventory is null)item.Destroy();
                }catch(Exception error){check(false,mode+": cleanup "+error.Message);}
            }
        }
    }
    private static void Put(GameInventory inventory,GameItem item,int count)
    {
        var marker=inventory.TryFindOneValidInventorySlot(item);
        if(marker?.IsValid()!=true||marker.TryAcceptOnce(count)!=count)throw new InvalidOperationException("native component fixture admission failed");
    }
}
