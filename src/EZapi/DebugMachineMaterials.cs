using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>仅临时图：原生整堆/拆堆保管、退回和提交清理；不改玩家库存，不保存。</summary>
internal static class DebugMachineMaterials
{
    internal static object? LastReport {get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static void Register()
    {
        var r=ItemApi.RegisterItem("ezapi.debug","ezapi.debug.material_stack",()=>{
            var item=ItemDirectory.CreateEmptyItem("ezapi.debug.material_stack");
            item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());item.SetName("EZapi原生堆叠检查材料");item.SetUnitCount(1);
            item.maxSlotAmountItemFunc=NativeDelegateBridge.ProbeAmount();item.mayStackItemFunc=NativeDelegateBridge.ProbeStack();return item;
        },new NativeItemOptions("EZapi原生堆叠检查材料",SourceName:"EZapi开发检查"));
        if(!r.Accepted)throw new InvalidOperationException(r.Reason);
    }
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();var passed=0;var failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        foreach(var mode in new[]{"whole-return","split-return","whole-consume","split-consume"}) {
            GameItem? machine=null,input=null;MachineMaterialEscrow? journal=null;bool consumed=false,whole=mode.StartsWith("whole",StringComparison.Ordinal);
            try {
                machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);MachineLayoutApi.TryGetInventory(machine,out var slots);
                input=ItemApi.Create("ezapi.debug.material_stack",true);
                // 测试夹具初始化数量；资源消费必须通过真实接纳拆堆，禁止直接改数量。
                input.SetUnitCount(5);
                var marker=slots.ItemInput.TryInventorySlot(input,5,input.modifiedShape,null);
                var valid=marker?.IsValid();var reported=valid==true?marker!.TryAcceptOnce(5):-1;
                details.Add(new{mode,phase="fixture",units=input.unitCount,valid,reported,max=input.MaxNumRemove(),markerNum=marker?.numTransfer,attached=input.parentInventory?.Pointer.ToInt64()});
                if(valid!=true||reported!=5)throw new InvalidOperationException("fixture stack admission");
                var before=InventoryChecks.Item(input);var run=NativeAccess.RequireRun();var graphBefore=InventoryChecks.Read(slots.ItemInput,run);
                var context=new MachineBatchContext(machine,new[]{new MachineItemTake(input,whole?5:2)},new Dictionary<string,GameItem>(),1);
                journal=new(context);Check(journal.Validate(),mode+": journal precheck");journal.Begin(machine,Array.Empty<GameItem>());
                Check(InventoryFreezeApi.IsFrozen(input)&&!input.MayRemove(),mode+": native input frozen before stage");
                journal.Stage();Check(journal.Verify(),mode+": staged exact quantity and identity");
                var afterStage=InventoryChecks.Read(slots.ItemInput,run);
                Check(whole?afterStage.Items.Count==0:afterStage.Items.Single().Units==3,mode+": real source quantity delta");
                if(mode.EndsWith("return",StringComparison.Ordinal)) {
                    var restored=journal.Restore();journal=null;
                    Check(restored&&InventoryChecks.Equal(graphBefore,InventoryChecks.Read(slots.ItemInput,run)),mode+": original native position identity and count restored");
                    Check(InventoryChecks.Item(input)==before,mode+": original item intact");
                } else {
                    // 在不再需要原材料的测试提交点清理。整堆Destroy后不再访问input包装对象。
                    consumed=true;journal.Consume();journal=null;if(whole)input=null;
                    Check(InventoryChecks.Equal(afterStage,InventoryChecks.Read(slots.ItemInput,run)),mode+": committed source unchanged by disposal");
                }
                Check(!InventoryFreezeApi.IsFrozen(machine),mode+": freeze released");
                details.Add(new{mode,before,afterStage,final=InventoryChecks.Read(slots.ItemInput,run)});
            }catch(Exception e){Check(false,mode+": "+e);MelonLogger.Error("[EZapi] Material probe: "+e);}
            finally {
                if(journal is not null&&!consumed)journal.Restore();
                if(consumed&&whole)input=null;
                if(input is not null){if(input.parentInventory is {} p)p.Expel(input);input.Destroy();}
                machine?.Destroy();
            }
        }
        GameItem? water=null;
        try {
            water=ItemApi.Create("ezapi.sample.water_container",true);WaterHelper.FillWithBaseWater(water);
            var before=Liquids.CaptureProduction(water);var id=before.Contents.First(p=>p.Parts>0).LiquidId;
            WaterHelper.AddLiquid(water,id,-1000);var after=Liquids.CaptureProduction(water);
            details.Add(new{mode="negative-liquid-observation",before,after});
            WaterHelper.EmptyContainer(water);WaterHelper.AddLiquid(water,id,1000);
            Check(Liquids.CaptureProduction(water).TotalParts==1000,"native positive component filling");
        }catch(Exception e){Check(false,"liquid observation: "+e);}
        finally{water?.Destroy();}
        foreach(var mode in new[]{"default-whole","default-split","default-return","default-partial-delivery","default-full","default-contents","default-notify","default-stage-failure","default-empty-input","default-component"}) {
            var made=new List<GameItem>();var products=new List<GameItem>();IDisposable? refusal=null;
            var dead=new HashSet<IntPtr>();GameItem? input=null;
            try {
                var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);made.Add(machine);MachineLayoutApi.TryGetInventory(machine,out var slots);
                input=ItemApi.Create("ezapi.debug.material_stack",true);made.Add(input);input.SetUnitCount(5);input.SetValue(13);
                input.state.GetTag("EZAPI_MATERIAL_PROBE").Enable().SetInt(42);input.SyncModifiedState();Put(slots.ItemInput,input,5);
                var source=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());
                var liquid=ItemApi.Create("ezapi.sample.water_container",true);made.Add(liquid);WaterHelper.FillWithBaseWater(liquid);Put(slots.LiquidInputs["water"],liquid,1);
                var beforeLiquid=Liquids.CaptureProduction(liquid);
                var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery,1);
                if(mode=="default-full") {
                    var fill=ItemDirectory.CreateEmptyItem("ezapi.debug.material_blocker");made.Add(fill);fill.SetShape(new GridShapeBuilder(3,3).SetDataFill(1).Build());GeneralHelper.SetItemOwned(fill,true);Put(slots.Output,fill,1);
                }
                var isolationCalls=0;var isolationLeaks=0;
                refusal=InventoryAdmissionApi.Register("ezapi.debug",machine.identifier,slots.Output.identifier,(_,grid)=>{
                    if(InventoryFreezeApi.IsFrozen(machine)){isolationCalls++;if(input.MayRemove()||battery.MayRemove()||slots.ItemInput.Expel(input))isolationLeaks++;}
                    return mode is not("default-return" or "default-partial-delivery")||input.unitCount==5||mode=="default-partial-delivery"&&grid.childItems.Count==0;
                });
                IDisposable? stageRefusal=null;GameItem? extra=null;
                if(mode=="default-stage-failure") {
                    extra=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(extra);
                    var m=slots.ItemInput.TryFindOneValidInventorySlot(extra)!;if(m.TryAcceptOnce(1)!=1)throw new InvalidOperationException("second material fixture");
                    source=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());
                    stageRefusal=InventoryAdmissionApi.Register("ezapi.debug","ezapi.internal.material_escrow","ezapi.internal.material_escrow.1",(_,_)=>false);
                }
                var notes=0;var whole=mode=="default-whole";var content=mode=="default-contents";
                var output=new MachineItemOutput(content?"ezapi.sample.water_container":"ezapi.debug.factory_item",mode=="default-partial-delivery"?2:1,PrepareItem:(_,p)=>{products.Add(p);made.Add(p);});
                if(content)output=output with {Contents=new[]{new MachineLiquidAmount(beforeLiquid.Contents.First(p=>p.Parts>0).LiquidId,1,ResolveValue:_=>8){ResolveQualityBasis=_=>3}}};
                var ingredients=mode=="default-stage-failure"?new[]{new MachineIngredient(input.identifier,2),new MachineIngredient(extra!.identifier)}:
                    mode=="default-empty-input"?Array.Empty<MachineIngredient>():new[]{new MachineIngredient(input.identifier,whole?5:2)};
                var recipe=new MachineRecipe("ezapi.debug.default_probe",ingredients,output) {
                    LiquidInputs=new[]{new MachineLiquidIngredient("water",1,mode=="default-component"?beforeLiquid.Contents.First(p=>p.Parts>0).LiquidId:null)},AfterCommitted=(_,_)=>{notes++;if(mode=="default-notify")throw new InvalidOperationException("expected default notification failure");}
                };
                MachineBatchResult result;
                try{result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7)).Process(machine);}finally{stageRefusal?.Dispose();}
                var committed=mode is "default-whole" or "default-split" or "default-contents" or "default-notify" or "default-empty-input" or "default-component";
                if(result.Committed&&whole)dead.Add(input.Pointer);
                if(result.Transaction?.Status is MachineTransactionStatus.Restored or MachineTransactionStatus.Rejected)foreach(var p in products)dead.Add(p.Pointer);
                details.Add(new{mode,result,source,finalSource=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun()),energy=Power.GetEnergy(battery),water=Liquids.CaptureProduction(liquid),notes});
                Check(result.Committed==committed,mode+": committed matches expected");
                Check(Power.GetEnergy(battery)==(committed?93:100),mode+": real power delta or restoration");
                Check(committed?Liquids.CaptureProduction(liquid).TotalParts==beforeLiquid.TotalParts-1000:LiquidMetadata.Same(beforeLiquid,Liquids.CaptureProduction(liquid)),mode+": real liquid delta or restoration");
                Check(committed?(whole?slots.ItemInput.childItems.Count==0:input.unitCount==(mode=="default-empty-input"?5:3)):InventoryChecks.Equal(source,InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun())),mode+": real input delta or exact restore");
                Check(notes==(committed?1:0),mode+": notification once after commit");
                Check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(liquid),mode+": no leaked freeze");
                if(committed||mode is "default-return" or "default-partial-delivery")Check(isolationCalls>0&&isolationLeaks==0,mode+": native output callback cannot remove other frozen materials or battery");
                if(!whole)Check(input.GetTagReadonly("EZAPI_MATERIAL_PROBE").GetInt()==42&&input.unitValue==13,mode+": surviving material tags and value preserved");
                if(mode is "default-return" or "default-partial-delivery" or "default-stage-failure")Check(result.Transaction?.Status==MachineTransactionStatus.Restored,mode+": native failure compensated");
                if(mode=="default-component")Check(LiquidMetadata.Same(LiquidCompositionMath.Consume(beforeLiquid,1000,beforeLiquid.Contents.First(p=>p.Parts>0).LiquidId),Liquids.CaptureProduction(liquid)),mode+": only selected native component debited");
                if(mode=="default-notify")Check(result.Transaction?.CompletionError is not null,"default-notify: still committed with CompletionError");
                if(content&&result.Committed)Check(Liquids.CaptureProduction(products[0]).Contents.Any(p=>p.Parts==1000&&p.Value==8&&p.QualityBasis==3),"default-contents: physical output and saved metadata");
            }catch(Exception e){Check(false,mode+": "+e);MelonLogger.Error("[EZapi] Default material probe: "+e);}
            finally {
                refusal?.Dispose();
                foreach(var item in made.AsEnumerable().Reverse())if(!dead.Contains(item.Pointer)){if(item.parentInventory is {} p)p.Expel(item);item.Destroy();}
            }
        }
        DebugComponentLiquids.Run(Check,details);
        DebugLiquidOutputs.Run(Check,details);
        DebugMachineConditions.Run(Check,details);
        DebugMaterialGraphs.Run(Check,details);
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details};EZapiBootstrap.WriteDiagnostics();
        return $"材料保管检查：{passed}通过，{failed}失败，已导出。";
    }
    private static void Put(GameInventory inventory,GameItem item,int count)
    {var m=inventory.TryInventorySlot(item,count,item.modifiedShape,null)??inventory.TryFindOneValidInventorySlot(item);if(m?.IsValid()!=true||m.TryAcceptOnce(count)!=count)throw new InvalidOperationException("material fixture admission: "+inventory.identifier);}
}
