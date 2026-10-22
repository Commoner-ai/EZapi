using System.Reflection;
using HarmonyLib;
using Il2Cpp;
namespace EZapi;

/// <summary>材料检查中集中执行的液体输出验收；只创建临时原生图，注入失败的补丁在finally释放。</summary>
internal static class DebugLiquidOutputs
{
    private static IntPtr _blockedVessel;private static int _fillCalls;
    private static bool FillPrefix(GameItem __0)=>__0.Pointer!=_blockedVessel||++_fillCalls!=2;
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"liquid-output-empty","liquid-output-mixture","liquid-output-dynamic","liquid-output-full","liquid-output-missing",
            "liquid-output-condition","liquid-output-rollback","liquid-output-partial-fill"}) {
            var made=new List<GameItem>();DefaultMachineTransaction? injected=null;HarmonyLib.Harmony? patch=null;
            try {
                var machine=ItemApi.Create("ezapi.debug.machine_liquid_probe",true);made.Add(machine);
                if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("liquid-output layout missing");
                var material=ItemApi.Create("ezapi.debug.material_stack",true);made.Add(material);material.SetUnitCount(5);Put(slots.ItemInput,material,5);
                var source=ItemApi.Create("ezapi.sample.water_container",true);made.Add(source);WaterHelper.EmptyContainer(source);Put(slots.LiquidInputs["water"],source,1);
                var catalog=Liquid.Liquids.ToArray();if(catalog.Length<2)throw new InvalidOperationException("two liquid types required");
                var selected=catalog[0].identifier;var other=catalog[1].identifier;
                WaterHelper.AddLiquid(source,selected,2000);WaterHelper.AddLiquid(source,other,1500);
                Annotate(source);var sourceBefore=Liquids.CaptureProduction(source);var sourceTags=NativeTagBackup.Capture(source.state);
                var target=ItemApi.Create("ezapi.sample.water_container",true);made.Add(target);WaterHelper.EmptyContainer(target);
                if(mode=="liquid-output-full")WaterHelper.FillWithBaseWater(target);
                else if(mode!="liquid-output-empty")WaterHelper.AddLiquid(target,other,500);
                Put(slots.Output,target,1);Annotate(target);
                if(mode=="liquid-output-missing"&&!slots.Output.Expel(target))throw new InvalidOperationException("missing fixture expel refused");
                var targetBefore=Liquids.CaptureProduction(target);var targetTags=NativeTagBackup.Capture(target.state);
                var pointer=target.Pointer;var instance=target.uniqueId;var state=target.state.Pointer;var dict=target.state.dict.Pointer;
                var battery=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery,1);
                var inputBefore=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());
                var additions=new[]{new MachineLiquidPart(selected,1000){Value=12,QualityBasis=4},new MachineLiquidPart(other,1000){Value=6,QualityBasis=2}};
                var expected=mode=="liquid-output-full"?targetBefore:LiquidCompositionMath.Add(targetBefore,additions);
                var notes=0;var resolves=0;var predicateCalls=0;
                var output=new MachineContainerOutput(new[]{new MachineLiquidAmount(selected,1,ResolveValue:_=>12){ResolveQualityBasis=_=>4},
                    new MachineLiquidAmount(other,1,ResolveValue:_=>6){ResolveQualityBasis=_=>2}}) {
                    Condition=_=>{predicateCalls++;return mode!="liquid-output-condition";},
                    ResolveContents=mode=="liquid-output-dynamic"?_=>{resolves++;return additions;}:null
                };
                var recipe=new MachineRecipe("ezapi.debug."+mode.Replace('-','_'),new[]{new MachineIngredient(material.identifier,2)},output) {
                    LiquidInputs=new[]{new MachineLiquidIngredient("water",1,selected)},
                    AfterCommitted=(_,products)=>{notes++;check(products.Count==0,mode+": no new physical item products");},
                    Transaction=mode=="liquid-output-rollback"?plan=>new RefuseVerify(injected=new DefaultMachineTransaction(plan)):null
                };
                if(mode=="liquid-output-partial-fill") {
                    var targetMethod=typeof(WaterHelper).GetMethod(nameof(WaterHelper.AddLiquid),BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(GameItem),typeof(string),typeof(int)},null)
                        ??throw new InvalidOperationException("exact AddLiquid target missing");
                    _blockedVessel=target.Pointer;_fillCalls=0;patch=new HarmonyLib.Harmony("ezapi.debug.liquid-output-failure");
                    patch.Patch(targetMethod,prefix:new HarmonyMethod(typeof(DebugLiquidOutputs).GetMethod(nameof(FillPrefix),BindingFlags.NonPublic|BindingFlags.Static)!));
                }
                var result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7)).Process(machine);
                var committed=mode is "liquid-output-empty" or "liquid-output-mixture" or "liquid-output-dynamic";
                var actual=Liquids.CaptureProduction(target);details.Add(new{mode,result,targetBefore,expected,actual,notes,resolves,predicateCalls,fillCalls=_fillCalls});
                check(result.Committed==committed,mode+": expected outcome");
                check(target.Pointer==pointer&&target.uniqueId==instance&&target.state.Pointer==state&&target.state.dict.Pointer==dict&&
                    target.parentInventory?.Pointer==(mode=="liquid-output-missing"?(IntPtr?)null:slots.Output.Pointer),mode+": existing vessel and native graph identity preserved");
                check(LiquidMetadata.Same(committed?expected:targetBefore,actual),mode+": exact output composition/value/quality or restoration");
                check(LiquidMetadata.Same(committed?LiquidCompositionMath.Consume(sourceBefore,1000,selected):sourceBefore,Liquids.CaptureProduction(source)),mode+": source liquid debit or restoration");
                check(Power.GetEnergy(battery)==(committed?93:100),mode+": exact power delta or restoration");
                check(committed?material.unitCount==3:InventoryChecks.Equal(inputBefore,InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun())),mode+": input conservation");
                check(notes==(committed?1:0),mode+": committed notification exactly once");
                check(result.Transaction?.Created.Count is null or 0,mode+": no item factory output receipt");
                check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(target),mode+": no leaked freeze");
                if(mode is "liquid-output-rollback" or "liquid-output-partial-fill") {
                    check(result.Transaction?.Status==MachineTransactionStatus.Restored,mode+": failed write compensated");
                    check(sourceTags.Matches(source.state)&&targetTags.Matches(target.state),mode+": complete original tag values restored");
                }
                if(mode=="liquid-output-partial-fill")check(_fillCalls==2,mode+": actual second native add refused after first succeeded");
                if(mode=="liquid-output-dynamic")check(resolves==1,mode+": resolved composition called once");
                if(mode=="liquid-output-condition")check(predicateCalls==1,mode+": false predicate refuses before transaction");
            }catch(Exception error){check(false,mode+": "+error);MelonLoader.MelonLogger.Error("[EZapi] Liquid output fixture: "+error);}
            finally {
                _blockedVessel=IntPtr.Zero;_fillCalls=0;patch?.UnpatchSelf();injected?.Dispose();
                foreach(var item in made.AsEnumerable().Reverse())try {
                    if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("fixture expel failed");
                    if(item.parentInventory is null)item.Destroy();
                }catch(Exception error){check(false,mode+": cleanup "+error.Message);}
            }
        }
    }
    private static void Annotate(GameItem item)
    {
        var p=MachineLiquidSnapshot.From(Liquids.Capture(item));
        var result=Liquids.SetProductionMetadata(item,new(p.CapacityParts,p.Contents.Select(c=>c with {Value=c.Parts/100m,QualityBasis=c.Parts/200m}).ToArray()));
        if(!result.Committed)throw new InvalidOperationException("fixture metadata refused: "+System.Text.Json.JsonSerializer.Serialize(result));
    }
    private static void Put(GameInventory inventory,GameItem item,int amount)
    {
        var marker=inventory.TryFindOneValidInventorySlot(item);
        if(marker?.IsValid()!=true||marker.TryAcceptOnce(amount)!=amount)throw new InvalidOperationException("liquid output fixture admission failed");
    }
    private sealed class RefuseVerify:IMachineBatchTransaction
    {
        private readonly DefaultMachineTransaction inner;
        internal RefuseVerify(DefaultMachineTransaction transaction)=>inner=transaction;
        public bool Validate()=>inner.Validate();public void Apply(IReadOnlyList<GameItem> products)=>inner.Apply(products);
        public bool Verify()=>false;public bool Restore()=>inner.Restore();public void Fault(string reason)=>inner.Fault(reason);
    }
}
