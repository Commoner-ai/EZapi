using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>一次按钮调用真实原版熔炉回调；所有物品临时创建并销毁，不调用保存或夜间事件。</summary>
internal static class DebugNativeFurnace
{
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请在测试档店铺中放下物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            Check(ExternalPowerRuntime.Status().Ready&&ExternalPowerRuntime.Status().Targets.Count==12,"twelve exact native hooks ready");
            foreach(var mode in new[]{"native","commit","zero","empty","insufficient","disconnected","validate","debit-throw","verify","late-verify","native-output-policy","dispose","old-output","full","old-destroy","restore-false",
                "nested-reject","nested-commit","nested-rollback","nested-metadata","nested-destroy","nested-frozen","old-nested-commit","old-nested-rollback","empty-window","nested-zero","nested-liquid","old-nested-destroy"}) {
                var roots=new List<GameItem>();var loose=new List<GameItem>();var descendants=new List<(GameItem Item,int Id)>();IDisposable? provider=null;IDisposable? preFrozen=null;
                try {
                    var machine=Items.Create("furnace",true);roots.Add(machine);GeneralHelper.SetItemOwned(machine,true);
                    var closure=machine.onCycleEndSlotItemFunc.Target.Cast<MachineFurnace.__c__DisplayClass0_0>();
                    var input=closure.itemInventoryLeft;var output=closure.itemInventoryRight;
                    var station=ItemApi.Create("ezapi.debug.machine_layout_probe",true);roots.Add(station);MachineLayoutApi.TryGetInventory(station,out var sourceSlots);
                    var host=sourceSlots.Output;var hostAdmit=host.mayInventoryAddItemFunc;var hostLock=host.overrideLockInsert;
                    try{host.mayInventoryAddItemFunc=null;host.overrideLockInsert=false;Put(host,machine);}finally{host.mayInventoryAddItemFunc=hostAdmit;host.overrideLockInsert=hostLock;}
                    roots.Remove(machine);roots.Add(machine);
                    var source=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(source);PowerHelper.InitPowerSourceItem(source,1000,1000);Put(sourceSlots.Battery!,source);loose.Remove(source);
                    var connector=ItemDirectory.CreateEmptyItem("ezapi.debug.furnace_"+mode);loose.Add(connector);connector.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());GeneralHelper.SetItemOwned(connector,false);
                    Probe? probe=null;var plans=0;var reject=false;GameItem? priorOutput=null;GameItem? nestedChild=null;GameItem? nestedGrand=null;
                    if(mode=="native")PowerHelper.InitPowerSourceItem(connector,1000,1000);
                    else {
                        provider=ExternalPowerApi.Register("ezapi.debug",connector.identifier,_=>mode=="disconnected"?-1:Power.GetEnergy(source),
                            (m,c,n)=>{plans++;if(m.Pointer!=machine.Pointer||c.Pointer!=connector.Pointer)throw new InvalidOperationException("wrong native furnace plan context");
                                return probe=new Probe(source,n,mode,()=>{if(mode=="native-output-policy")reject=true;if(mode=="dispose")provider!.Dispose();if(mode=="old-destroy"){priorOutput!.Destroy();throw new InvalidOperationException("expected old output deferred destruction");}
                                    if(nestedGrand is not null)Check(InventoryFreezeApi.IsFrozen(nestedGrand)&&!nestedGrand.MayRemove(),mode+": descendant frozen inside provider callback");
                                    if(mode=="nested-metadata"){nestedGrand!.SetValue(999);nestedGrand.state.GetTag("EZAPI_NESTED_METADATA").Enable().SetInt(8);nestedGrand.SyncModifiedState();}
                                    if(mode=="nested-liquid")WaterHelper.InitLiquidContainerItem(nestedGrand!,20,0,false,false,false,false,3);
                                    if(mode=="nested-destroy")nestedGrand!.Destroy();
                                    if(mode=="old-nested-destroy"){priorOutput!.Destroy();throw new InvalidOperationException("expected nested old output deferred destruction");}
                                });},mode.StartsWith("nested-",StringComparison.Ordinal)&&mode!="nested-reject"||mode=="empty-window"?MachineMaterialContentsPolicy.ConsumeGraph:MachineMaterialContentsPolicy.Reject);
                        ExternalPowerApi.InitializeConnector(connector);
                    }
                    GeneralHelper.SetItemOwned(connector,true);var battery=MachineHelper.GetBatterySlot(machine);Put(battery,connector);loose.Remove(connector);
                    var material=Items.Create("scrap_metal",true);loose.Add(material);GeneralHelper.SetItemOwned(material,true);
                    material.SetValue(100);material.state.GetTag("EZAPI_FURNACE_TEST").Enable().SetString("original input");material.SyncModifiedState();
                    GameItem? material2=null;
                    if(mode!="empty"){
                        Put(input,material);loose.Remove(material);
                        material2=Items.Create("scrap_metal",true);loose.Add(material2);material2.SetValue(100);
                        material2.state.GetTag("EZAPI_FURNACE_TEST").Enable().SetString("second input");material2.SyncModifiedState();
                        Put(input,material2);loose.Remove(material2);
                    }
                    GameItem? old=null;
                    if(mode is "old-output" or "late-verify" or "native-output-policy" or "old-destroy" or "old-nested-commit" or "old-nested-rollback" or "old-nested-destroy") {
                        old=Items.Create("metal_ingot",true);loose.Add(old);GeneralHelper.SetItemOwned(old,true);priorOutput=old;var admit=output.mayInventoryAddItemFunc;var locked=output.overrideLockInsert;
                        try{output.mayInventoryAddItemFunc=null;output.overrideLockInsert=false;Put(output,old);}finally{output.mayInventoryAddItemFunc=admit;output.overrideLockInsert=locked;}
                        loose.Remove(old);
                        old.state.GetTag("EZAPI_FURNACE_TEST").Enable().SetString("original output");old.SyncModifiedState();
                    }
                    MachineMaterialGraph? nestedGraph=null;int[] nestedIds=Array.Empty<int>();
                    if(mode.StartsWith("nested-",StringComparison.Ordinal)||mode.StartsWith("old-nested-",StringComparison.Ordinal)||mode=="empty-window") {
                        var target=old??material;
                        var window=new PixelWindow(false,"EZapi熔炉内装物测试");var grid=new GameGridInventory(6,6);target.SetContentWindow(window);
                        if(!window.Attach(grid.Cast<PixelElement>()))throw new InvalidOperationException("furnace contents fixture window attach failed");
                        if(mode!="empty-window") {
                            nestedChild=ItemApi.Create("ezapi.sample.storage_box",true);descendants.Add((nestedChild,nestedChild.uniqueId));nestedChild.EnableTag("NESTABLE_DEVICE_TAG");nestedChild.SyncModifiedState();Put(grid,nestedChild);
                            nestedGrand=ItemApi.Create("ezapi.sample.water_container",true);descendants.Add((nestedGrand,nestedGrand.uniqueId));Put(nestedChild.contentWindow.childElement.Cast<GameGridInventory>(),nestedGrand);
                            nestedGraph=MachineMaterialGraph.Capture(target,false);nestedIds=new[]{nestedChild.uniqueId,nestedGrand.uniqueId};
                            if(mode=="nested-frozen")preFrozen=InventoryFreezeApi.TryAcquire(new[]{nestedGrand})??throw new InvalidOperationException("fixture child freeze failed");
                        }else nestedGraph=MachineMaterialGraph.Capture(target,false);
                    }
                    if(mode is "zero" or "nested-zero") {MachineryHelper.InitMachinery(machine,0,false);ModuleHelper.ApplyModifiedPower(machine);}
                    if(mode=="insufficient")PowerHelper.SetPowerSourceAt(source,0);
                    output.mayInventoryAddItemFunc=(Func<GameItem,GameInventory,bool>)((_,_)=>!reject);
                    if(mode=="full") {
                        var may=output.mayInventoryAddItemFunc;var locked=output.overrideLockInsert;
                        try {output.mayInventoryAddItemFunc=null;output.overrideLockInsert=false;
                            for(var n=0;n<54;n++){var filler=ItemApi.Create("ezapi.debug.factory_item",true);loose.Add(filler);Put(output,filler);loose.Remove(filler);}
                        }finally{output.mayInventoryAddItemFunc=may;output.overrideLockInsert=locked;}
                    }
                    var beforeLiquid=nestedGrand is null?null:Liquids.CaptureProduction(nestedGrand);
                    var beforeIn=InventoryChecks.Read(input,NativeAccess.RequireRun());var beforeOut=InventoryChecks.Read(output,NativeAccess.RequireRun());
                    var original=mode=="empty"?null:new NativeFurnacePower.ItemState(material,input);
                    var secondState=material2 is null?null:new NativeFurnacePower.ItemState(material2,input);
                    var oldState=old is null?null:new NativeFurnacePower.ItemState(old,output);
                    MachineryHelper.UpdateProcessingTypeMachine(machine);
                    var tooltips=new{machine=ItemInspection.BuildTooltip(machine).nakedText,input=ItemInspection.BuildTooltip(material).nakedText};
                    var directoryIds=new List<string>();foreach(var n in DirectoryMaster.GetDictionaryClassNameList<GameItem>())foreach(var id in DirectoryMaster.GetIdentifierList<GameItem>(n))if(id.Contains("furnace",StringComparison.OrdinalIgnoreCase)||id.Contains("ore",StringComparison.OrdinalIgnoreCase))directoryIds.Add(id);
                    

                    var pre=new {value=material.unitValue,power=MachineHelper.CanPower(machine),toggle=MachineryHelper.IsMachineryToggleOn(machine),processing=MachineryHelper.IsProcessingActive(machine),
                        machineTags=Tags(machine),
                        inputTags=Tags(material),
                        inputPointer=input.Pointer.ToInt64(),inputGrid=InventoryChecks.Read(input,NativeAccess.RequireRun()),outputGrid=InventoryChecks.Read(output,NativeAccess.RequireRun())};
                    var cost=Power.GetMachineCost(machine);var before=mode=="native"?Power.GetEnergy(connector):Power.GetEnergy(source);
                    try{machine.onCycleEndSlotItemFunc.Invoke(machine,machine.parentInventory!,machine.GetIncompleteSlotInInventory());}finally{preFrozen?.Dispose();}
                    var after=mode=="native"?Power.GetEnergy(connector):Power.GetEnergy(source);
                    var success=mode is "native" or "commit" or "zero" or "old-output" or "native-output-policy" or "nested-commit" or "old-nested-commit" or "empty-window" or "nested-zero";
                    if(nestedGraph is not null) {
                        if(mode is "nested-commit" or "nested-zero")Check(NativeFurnacePower.LastDestroyedContents.SequenceEqual(nestedIds.Reverse()),mode+": actual descendant destruction receipts deepest first");
                        else if(mode!="empty-window"){
                            Check(nestedGraph.Matches(),mode+": exact original descendant identities topology tags values liquid and inventory preserved");
                            Check(beforeLiquid is not null&&LiquidMetadata.Same(beforeLiquid,Liquids.CaptureProduction(nestedGrand!)),mode+": native liquid composition capacity purity and condition unchanged");
                        }
                        if(mode is "nested-reject" or "nested-frozen")Check(plans==0,mode+": rejected before provider planning or debit");
                        if(mode is "old-nested-commit" or "old-nested-rollback" or "old-nested-destroy")Check(oldState?.Contents is not null,mode+": old output owns contents journal under default Reject material policy");
                    }
                    details.Add(new{mode,pre,tooltips,directoryIds,cost,before,after,plans,debits=probe?.Debits??0,restores=probe?.Restores??0,verifies=probe?.Verifies??0,
                        inputBefore=beforeIn,outputBefore=beforeOut,inputAfter=InventoryChecks.Read(input,NativeAccess.RequireRun()),outputAfter=InventoryChecks.Read(output,NativeAccess.RequireRun()),
                        quarantine=MachineFaultGuard.Blocked(machine),native=NativeFurnacePower.Report(),nestedIds,
                        early=machine.onCycleEndEarlySlotItemFunc is not null,normal=machine.onCycleEndSlotItemFunc is not null,late=machine.onCycleEndLateSlotItemFunc is not null});
                    Check(after==before-(success?cost:0),mode+": real power delta");
                    Check(success?input.childItems.Count==0:InventoryChecks.Equal(beforeIn,InventoryChecks.Read(input,NativeAccess.RequireRun())),mode+": original input commit or exact restore");
                    Check(success?output.childItems.Count>0:InventoryChecks.Equal(beforeOut,InventoryChecks.Read(output,NativeAccess.RequireRun())),mode+": output commit or exact restore");
                    if(!success&&original is not null)Check(original.Matches(),mode+": original input tags, value, slot, handler and position restored");
                    if(!success&&secondState is not null)Check(secondState.Matches(),mode+": second input exact original restored");
                    if(!success&&oldState is not null)Check(oldState.Matches(),mode+": original output tags, value, slot, handler and position restored");
                    Check(MachineFaultGuard.Blocked(machine)==(mode=="restore-false"),mode+": expected quarantine state");
                    if(mode=="restore-false") {
                        var prior=probe!.Debits;machine.onCycleEndSlotItemFunc!.Invoke(machine,machine.parentInventory!,machine.GetIncompleteSlotInInventory());
                        Check(probe.Debits==prior&&Power.GetEnergy(source)==before,"quarantined furnace refuses next native callback");
                    }
                    Check((probe?.Debits??0)<=1&&(probe?.Restores??0)<=1,mode+": provider debit and restore at most once");
                    if(mode is "debit-throw" or "verify" or "late-verify" or "dispose" or "old-destroy" or "restore-false" or "nested-rollback" or "nested-metadata" or "nested-liquid" or "nested-destroy" or "old-nested-rollback" or "old-nested-destroy")Check(probe?.Restores==1,mode+": attempted debit restored once");
                    if(mode is "zero" or "nested-zero" or "empty" or "insufficient" or "disconnected" or "native")Check(plans==0,mode+": no provider transaction needed");
                }catch(Exception e){Check(false,mode+": execution "+e);MelonLogger.Error("[EZapi] Furnace fixture "+mode+": "+e);}
                finally {
                    preFrozen?.Dispose();provider?.Dispose();
                    var destroyed=NativeFurnacePower.LastDestroyedContents.ToHashSet();
                    foreach(var entry in descendants.AsEnumerable().Reverse())if(!destroyed.Contains(entry.Id))try{if(entry.Item.parentInventory is {} parent)parent.Expel(entry.Item);if(entry.Item.parentInventory is null)entry.Item.Destroy();}catch(Exception e){Check(false,mode+": nested cleanup "+e.Message);}
                    foreach(var item in loose.Concat(roots).Reverse())try{if(item.parentInventory is {} parent)parent.Expel(item);if(item.parentInventory is null)item.Destroy();}catch(Exception e){Check(false,mode+": cleanup "+e.Message);}
                }
            }
        }catch(Exception e){Check(false,"execution "+e);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="real native Furnace callback on temporary native graphs, no save/night event; independent energy source; no Forge loaded"};
        EZapiBootstrap.WriteDiagnostics();return $"原版熔炉：{passed}通过，{failed}失败。";
    }
    private sealed class Probe:IPowerTransaction
    {
        private readonly GameItem _source;private readonly int _before,_amount;private readonly string _mode;private readonly Action _after;
        internal int Debits,Restores,Verifies;
        internal Probe(GameItem source,int amount,string mode,Action after)=>(_source,_before,_amount,_mode,_after)=(source,Power.GetEnergy(source),amount,mode,after);
        public bool Validate()=>_mode!="validate"&&Power.GetEnergy(_source)==_before;
        public void Debit(){Debits++;if(!PowerHelper.DrawPowerSource(_source,_amount))throw new InvalidOperationException("source debit rejected");_after();if(_mode=="debit-throw")throw new InvalidOperationException("expected furnace Debit failure");}
        public bool Verify(){Verifies++;return _mode!="verify"&&(_mode is not ("late-verify" or "restore-false" or "nested-rollback" or "old-nested-rollback")||Verifies<3)&&Power.GetEnergy(_source)==_before-_amount;}
        public bool Restore(){Restores++;PowerHelper.SetPowerSourceAt(_source,_before);return _mode!="restore-false"&&Power.GetEnergy(_source)==_before;}
        public void Fault(string reason)=>MelonLogger.Warning("[EZapi] Furnace fixture fault: "+reason);
    }
    private static object Tags(GameItem item){var list=new List<object>();foreach(var p in item.state.dict)list.Add(new{id=p.Key,value=p.Value.valueString,enabled=p.Value.IsEnabled(),i=p.Value.GetInt(),b=p.Value.GetBool()});return list;}
    private static void Put(GameInventory inventory,GameItem item)
    {
        var slot=inventory.TryFindOneValidInventorySlot(item);
        if(slot?.IsValid()!=true||slot.targetItem is not null)throw new InvalidOperationException("furnace fixture slot rejected: "+inventory.identifier+" / "+item.identifier);
        try{slot.TryAcceptOnce(item.unitCount);}catch when(item.parentInventory?.Pointer==inventory.Pointer){}
        if(item.parentInventory?.Pointer!=inventory.Pointer)throw new InvalidOperationException("furnace fixture native admission mismatch");
    }
}


