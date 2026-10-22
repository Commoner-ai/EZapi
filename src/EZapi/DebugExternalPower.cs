using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>显式按钮集中验证外部真实原生电源、正常电池、原生拦截、资源恢复与注册释放。所有图均临时，不保存。</summary>
internal static class DebugExternalPower
{
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            Check(ExternalPowerApi.IsAvailable,"all twelve native power hooks installed");
            foreach(var mode in new[]{"commit","zero","disconnected","insufficient","validate","debit-throw","verify","delivery","dispose","native"}) {
                var roots=new List<GameItem>();var loose=new List<GameItem>();IDisposable? provider=null,admission=null;
                try {
                    var machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);roots.Add(machine);MachineLayoutApi.TryGetInventory(machine,out var slots);
                    var station=ItemApi.Create("ezapi.debug.machine_layout_probe",true);roots.Add(station);MachineLayoutApi.TryGetInventory(station,out var sourceSlots);
                    var source=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(source);PowerHelper.InitPowerSourceItem(source,100,100);Put(sourceSlots.Battery!,source);loose.Remove(source);
                    if(mode=="insufficient")PowerHelper.SetPowerSourceAt(source,0);
                    var connector=ItemDirectory.CreateEmptyItem("ezapi.debug.external_"+mode);loose.Add(connector);
                    connector.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                    if(mode=="native")PowerHelper.InitPowerSourceItem(connector,100,100);
                    var input=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(input);Put(slots.ItemInput,input);loose.Remove(input);
                    var calls=0;var rejectOutput=false;Probe? probe=null;
                    if(mode!="native")provider=ExternalPowerApi.Register("ezapi.debug",connector.identifier,
                        _=>mode=="disconnected"?-1:Power.GetEnergy(source),
                        (m,c,amount)=>{calls++;if(m.Pointer!=machine.Pointer||c.Pointer!=connector.Pointer)throw new InvalidOperationException("provider received different machine/connector");
                            return probe=new Probe(source,amount,mode,()=>{rejectOutput=mode=="delivery";if(mode=="dispose")provider!.Dispose();});});
                    if(mode!="native"){GeneralHelper.SetItemOwned(connector,false);ExternalPowerApi.InitializeConnector(connector);}
                    GeneralHelper.SetItemOwned(connector,true);Put(slots.Battery!,connector);loose.Remove(connector);
                    admission=InventoryAdmissionApi.Register("ezapi.debug",machine.identifier,slots.Output.identifier,(_,_)=>!rejectOutput);
                    var energy=mode=="native"?100:Power.GetEnergy(source);var cost=mode=="zero"?0:7;
                    if(mode=="commit") {
                        Check(connector.GetTagReadonly("EZAPI_EXTERNAL_CONNECTOR_V1")?.IsEnabled()==true,"fresh connector initialized through official helper with scoped permit");
                        var initializationRejected=false;
                        try{ExternalPowerApi.InitializeConnector(connector);}catch(InvalidOperationException){initializationRejected=true;}
                        Check(initializationRejected,"owned installed connector cannot be initialized again");
                        Check(ExternalPowerApi.IsConnector(connector)&&ExternalPowerApi.IsConnected(connector),"exact registered connector connected");
                        var tooltip=ItemInspection.BuildTooltip(connector).nakedText;
                        Check(tooltip.Contains("外部电源：已连接",StringComparison.Ordinal)&&tooltip.Contains("100",StringComparison.Ordinal)&&tooltip.Contains("不能充电",StringComparison.Ordinal),"native tooltip reports actual external energy and no charging");
                        Check(PowerHelper.GetAvailableEnergyFromItem(connector,true)==100&&PowerHelper.GetAvailableEnergyFromItem(connector,false)==100,"native bandwidth and total queries delegate to actual external source");
                        Check(PowerHelper.CanDrawPowerSource(connector,7)&&!PowerHelper.CanDrawPowerSource(connector,101)&&!PowerHelper.CanDrawPowerSource(connector,-1),"native CanDraw checks external amount bounds");
                        Check(!PowerHelper.DrawPowerSource(connector,7)&&PowerHelper.DrawPowerSource(connector,0),"unscoped positive native debit rejected; connected zero accepted");
                        Check(Power.TryDebit(connector,7).Status==EnergyDebitStatus.Rejected,"standalone debit refuses missing machine context");
                        PowerHelper.SetPowerSourceAt(connector,13);PowerHelper.InitPowerSourceItem(connector,200,1);PowerHelper.ChargeBattery(connector);PowerHelper.ChargeBattery(connector,10,false);
                        Check(PowerHelper.TryRemoveEnergy(connector,5,false)==0&&PowerHelper.IsPowerSourceFull(connector),"direct removal and recharge paths guarded");
                        Check(Power.GetEnergy(source)==100&&calls==0,"queries and rejected native writes never plan or debit source");
                    }
                    var recipe=new MachineRecipe("ezapi.debug.external_batch",new[]{new MachineIngredient("ezapi.debug.factory_item")},new("ezapi.debug.factory_item"));
                    var processor=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(cost));
                    var result=processor.Process(machine);var success=mode is "commit" or "zero" or "native";
                    var actual=mode=="native"?Power.GetEnergy(connector):Power.GetEnergy(source);
                    details.Add(new{mode,result,energyBefore=energy,energyAfter=actual,calls,debits=probe?.Debits??0,restores=probe?.Restores??0,inputCount=slots.ItemInput.childItems.Count,outputCount=slots.Output.childItems.Count});
                    Check(result.Committed==success,mode+": expected batch outcome");
                    Check(actual==energy-(success?cost:0),mode+": exact real source energy readback");
                    Check(slots.ItemInput.childItems.Count==(success?0:1)&&slots.Output.childItems.Count==(success?1:0),mode+": materials and outputs commit or restore");
                    Check((probe?.Debits??0)==(mode is "commit" or "debit-throw" or "verify" or "delivery" or "dispose"?1:0),mode+": exactly one provider Debit when applicable");
                    Check((probe?.Restores??0)==(mode is "debit-throw" or "verify" or "delivery" or "dispose"?1:0),mode+": exactly one provider Restore after attempted failed debit");
                    Check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(station),mode+": all machine freezes released");
                    if(mode=="disconnected")Check(!ExternalPowerApi.IsConnected(connector)&&ExternalPowerApi.GetEnergy(connector)==0&&!PowerHelper.CanDrawPowerSource(connector,0),"disconnected differs from connected zero energy");
                    if(mode=="insufficient"||mode=="disconnected")Check(calls==0,mode+": insufficient selection does not open provider plan");
                    if(mode=="zero")Check(calls==0&&probe is null,"zero cost avoids creating a provider transaction");
                    if(mode is "debit-throw" or "verify" or "delivery" or "dispose")Check(result.Transaction?.Status==MachineTransactionStatus.Restored,mode+": recoverable failure reports Restored");
                    if(mode=="commit") {
                        provider!.Dispose();Check(!ExternalPowerApi.IsConnector(connector),"disposed registration no longer routes connector");
                        // 原生标签仍0，说明原生Set/Init/Charge没有写入连接器；读取后才销毁临时图。
                        Check(Power.GetEnergy(connector)==0,"connector native storage unchanged by guarded operations");
                        using var replacement=ExternalPowerApi.Register("ezapi.debug",connector.identifier,_=>23,(_,_,_)=>throw new InvalidOperationException("unused"));
                        Check(ExternalPowerApi.GetEnergy(connector)==23,"same ID can be registered after previous lease released");
                    }
                }finally {
                    admission?.Dispose();provider?.Dispose();
                    foreach(var item in loose.Concat(roots).Reverse())try{if(item.parentInventory is {} parent)parent.Expel(item);if(item.parentInventory is null)item.Destroy();}catch(Exception e){Check(false,mode+": cleanup "+e.Message);}
                }
            }
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] External power probe: "+e);}
        LastReport=new{at=DateTimeOffset.Now,kind="temporary-native-regression",passed,failed,checks,details,
            scope="real native external battery in a separate machine; EZapi synchronous batch; no save, no vanilla furnace processing"};
        EZapiBootstrap.WriteDiagnostics();return $"外部电力检查：{passed}通过，{failed}失败，已导出。";
    }
    private sealed class Probe:IPowerTransaction
    {
        private readonly GameItem _source;private readonly int _before,_amount;private readonly string _mode;private readonly Action _after;
        internal int Debits,Restores;
        internal Probe(GameItem source,int amount,string mode,Action after)=>(_source,_before,_amount,_mode,_after)=(source,Power.GetEnergy(source),amount,mode,after);
        public bool Validate()=>_mode!="validate"&&Power.GetEnergy(_source)==_before;
        public void Debit(){Debits++;if(!PowerHelper.DrawPowerSource(_source,_amount))throw new InvalidOperationException("external source native debit rejected");_after();if(_mode=="debit-throw")throw new InvalidOperationException("expected external debit exception");}
        public bool Verify()=>_mode!="verify"&&Power.GetEnergy(_source)==_before-_amount;
        public bool Restore(){Restores++;PowerHelper.SetPowerSourceAt(_source,_before);return Power.GetEnergy(_source)==_before;}
        public void Fault(string reason)=>MelonLogger.Warning("[EZapi] Expected external fixture fault: "+reason);
    }
    private static void Put(GameInventory inventory,GameItem item)
    {
        var slot=inventory.TryFindOneValidInventorySlot(item);
        if(slot?.IsValid()!=true||slot.targetItem is not null)throw new InvalidOperationException("external fixture native slot");
        try{slot.TryAcceptOnce(1);}catch when(item.parentInventory?.Pointer==inventory.Pointer){}
        if(item.parentInventory?.Pointer!=inventory.Pointer)throw new InvalidOperationException("external fixture admission readback");
    }
}
