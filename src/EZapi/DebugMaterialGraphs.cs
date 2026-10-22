using Il2Cpp;
namespace EZapi;

/// <summary>临时三层材料图的原生事务检查。仅读取纯托管清理收据，销毁后不再解引用包装对象；不安装销毁钩子。</summary>
internal static class DebugMaterialGraphs
{
    private sealed class Owned
    {
        internal readonly GameItem Item;internal readonly IntPtr Pointer;internal readonly int Id;internal bool Retired;
        internal Owned(GameItem item)=>(Item,Pointer,Id)=(item,item.Pointer,item.uniqueId);
    }
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"graph-reject","graph-consume","graph-consume-auto","graph-output-refused","graph-metadata-rollback","graph-cleanup-fault","graph-partial-output","graph-verify-rollback",
            "graph-stage-refused","graph-frozen-child","graph-readonly-resolver","graph-prepare-mutation"}) {
            var made=new List<Owned>();var leases=new List<IDisposable>();DefaultMachineTransaction? injected=null;
            Owned Track(GameItem item){var owned=new Owned(item);made.Add(owned);return owned;}
            try {
                MelonLoader.MelonLogger.Msg("[EZapi] Material graph phase: "+mode+" create");
                var machine=Track(ItemApi.Create("ezapi.debug.machine_layout_probe",true)).Item;
                if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("material graph layout absent");
                var root=Track(ItemApi.Create("ezapi.sample.storage_box",true));
                var child=Track(ItemApi.Create("ezapi.sample.storage_box",true));
                var grand=Track(ItemApi.Create("ezapi.sample.water_container",true));
                // 官方原生容器禁止普通容器嵌套；夹具显式声明可嵌套设备，不绕过原生准入。
                foreach(var item in new[]{root.Item,child.Item}){item.EnableTag("NESTABLE_DEVICE_TAG");item.SyncModifiedState();}
                Put(slots.ItemInput,root.Item);Put(Grid(root.Item),child.Item);Put(Grid(child.Item),grand.Item);
                MelonLoader.MelonLogger.Msg("[EZapi] Material graph phase: "+mode+" admitted");
                var battery=Track(ItemApi.Create("ezapi.debug.factory_item",true)).Item;
                PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery);
                foreach(var owned in new[]{root,child,grand}){
                    owned.Item.state.GetTag("EZAPI_GRAPH_PROBE").Enable().SetInt(owned.Id);owned.Item.SyncModifiedState();owned.Item.SetValue(13);
                }
                WaterHelper.EmptyContainer(grand.Item);WaterHelper.AddLiquid(grand.Item,Liquid.Liquids[0].identifier,2000);
                var graph=MachineMaterialGraph.Capture(root.Item)??throw new InvalidOperationException("three-level material graph absent");
                MelonLoader.MelonLogger.Msg("[EZapi] Material graph phase: "+mode+" captured");
                var source=InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun());var liquid=Liquids.CaptureProduction(grand.Item);var grandValue=grand.Item.unitValue;
                check(graph.ItemCount==3&&graph.Matches(),mode+": three original native material instances captured");
                var notes=0;var isolationCalls=0;var isolationLeaks=0;
                leases.Add(InventoryAdmissionApi.Register("ezapi.graph-test",machine.identifier,slots.Output.identifier,(_,grid)=>{
                    var staged=root.Item.parentInventory?.Pointer!=slots.ItemInput.Pointer;
                    if(staged){isolationCalls++;if(grand.Item.MayRemove()||Grid(child.Item).Expel(grand.Item))isolationLeaks++;}
                    if(staged&&mode=="graph-metadata-rollback"){
                        child.Item.SetValue(99);grand.Item.state.GetTag("EZAPI_GRAPH_PROBE").SetInt(-1);grand.Item.SyncModifiedState();return false;
                    }
                    return !staged||mode!="graph-output-refused"&&(mode!="graph-partial-output"||grid.childItems.Count==0);
                }));
                if(mode=="graph-stage-refused")leases.Add(InventoryAdmissionApi.Register("ezapi.graph-test","ezapi.internal.material_escrow","ezapi.internal.material_escrow.0",(_,_)=>false));
                if(mode=="graph-frozen-child")leases.Add(InventoryFreezeApi.TryAcquire(new[]{grand.Item})??throw new InvalidOperationException("descendant fixture freeze unavailable"));
                var output=new MachineItemOutput("ezapi.debug.factory_item",mode=="graph-partial-output"?2:1,PrepareItem:(_,item)=>{
                    Track(item);if(mode=="graph-prepare-mutation")grand.Item.SetValue(99);
                }){ResolveItemId=mode=="graph-readonly-resolver"?_=>{grand.Item.SetValue(99);return "ezapi.debug.factory_item";}:null};
                var recipe=new MachineRecipe("ezapi.debug."+mode.Replace('-','_'),new[]{new MachineIngredient(root.Item.identifier){WholeStack=true}},output){
                    MaterialContents=mode=="graph-reject"?MachineMaterialContentsPolicy.Reject:MachineMaterialContentsPolicy.ConsumeGraph,
                    Transaction=mode=="graph-verify-rollback"?plan=>new RefuseVerify(injected=new DefaultMachineTransaction(plan)):
                        mode is "graph-consume" or "graph-cleanup-fault"?plan=>injected=new DefaultMachineTransaction(plan):null,
                    AfterCommitted=(_,_)=>{
                        try{if(mode=="graph-cleanup-fault")child.Item.SetValue(99);if(mode is "graph-consume" or "graph-cleanup-fault")injected!.Complete();}
                        finally{notes++;}
                    }
                };
                var result=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",machine.identifier,new[]{recipe},new(7)).Process(machine);
                MelonLoader.MelonLogger.Msg("[EZapi] Material graph phase: "+mode+" processed "+System.Text.Json.JsonSerializer.Serialize(result));
                var consumed=mode is "graph-consume" or "graph-consume-auto" or "graph-cleanup-fault";var illegal=mode is "graph-readonly-resolver" or "graph-prepare-mutation";
                var order=injected?.DestroyedGraphInstanceIds.ToArray()??Array.Empty<int>();
                // 自动路径清理报错时也不猜测哪些包装对象仍活着，保留不确定的临时图并明确让断言失败。
                foreach(var owned in made)if(order.Contains(owned.Id)||mode=="graph-consume-auto"&&result.Committed&&(owned==root||owned==child||owned==grand)||
                    result.Transaction?.Status is MachineTransactionStatus.Rejected or MachineTransactionStatus.Restored&&result.Transaction.Created.Any(p=>p.InstanceId==owned.Id))owned.Retired=true;
                check(result.Committed==consumed,mode+": expected commit outcome");
                check(Power.GetEnergy(battery)==(consumed?93:100),mode+": exact energy delta or restoration");
                check(notes==(consumed?1:0),mode+": notification only once after commit");
                if(mode=="graph-cleanup-fault"){
                    check(result.Transaction?.CompletionError is not null&&result.Transaction.MayRetry==false,mode+": cleanup error remains committed without retry");
                    check(order.Length==0&&!root.Retired&&!child.Retired&&!grand.Retired&&SaveGraphApi.Items(root.Item).Count==3,mode+": graph retained without partial destruction");
                    check(slots.ItemInput.childItems.Count==0&&slots.Output.childItems.Count==1,mode+": no refund or duplicate delivery after commit");
                    var escrow=root.Item.parentInventory?.GetParentItem()??throw new InvalidOperationException("retained escrow absent");
                    check(escrow.identifier=="ezapi.internal.material_escrow",mode+": remaining materials preserved in escrow");
                    // 只清理本次临时夹具；正式事务不会自动处理不确定的提交后资源。
                    made.Insert(0,new Owned(escrow));
                }else if(consumed){
                    check(result.Transaction?.CompletionError is null,mode+": complete committed cleanup without errors");
                    if(mode=="graph-consume")check(order.SequenceEqual(new[]{grand.Id,child.Id,root.Id}),mode+": native Destroy completed deepest first exactly once");
                    check(root.Retired&&child.Retired&&grand.Retired&&slots.ItemInput.childItems.Count==0&&slots.Output.childItems.Count==1,mode+": full material graph consumed and output delivered");
                }else{
                    check(!root.Retired&&!child.Retired&&!grand.Retired,mode+": no original material destroyed");
                    check(InventoryChecks.Equal(source,InventoryChecks.Read(slots.ItemInput,NativeAccess.RequireRun())),mode+": original root instance and source placement intact");
                    check(LiquidMetadata.Same(liquid,Liquids.CaptureProduction(grand.Item)),mode+": nested liquid contents unchanged");
                    if(illegal){check(!graph.Matches()&&(result.Error is not null||result.Transaction?.Status==MachineTransactionStatus.Rejected),mode+": forbidden descendant mutation detected before Apply");grand.Item.SetValue(grandValue);}
                    check(graph.Matches(),mode+": original child identities topology tags values and positions intact");
                    check(slots.Output.childItems.Count==0,mode+": failed output fully removed");
                    if(mode is "graph-output-refused" or "graph-metadata-rollback" or "graph-partial-output" or "graph-verify-rollback" or "graph-stage-refused")check(result.Transaction?.Status==MachineTransactionStatus.Restored,mode+": native failure fully compensated");
                }
                if(consumed||mode is "graph-output-refused" or "graph-partial-output")check(isolationCalls>0&&isolationLeaks==0,mode+": output callback cannot remove frozen descendant");
                if(mode!="graph-frozen-child")check(!InventoryFreezeApi.IsFrozen(machine)&&!InventoryFreezeApi.IsFrozen(battery)&&(!grand.Retired&&!InventoryFreezeApi.IsFrozen(grand.Item)||grand.Retired),mode+": transaction freeze released");
                details.Add(new{mode,result,materialIds=new[]{root.Id,child.Id,grand.Id},destroyOrder=order,notes,isolationCalls,isolationLeaks});
            }catch(Exception error){check(false,mode+": "+error);MelonLoader.MelonLogger.Error("[EZapi] Material graph fixture: "+error);}
            finally {
                MelonLoader.MelonLogger.Msg("[EZapi] Material graph phase: "+mode+" cleanup");
                foreach(var lease in leases.AsEnumerable().Reverse())lease.Dispose();injected?.Dispose();
                // 最深层先清理；已经确认销毁的对象绝不访问其Pointer或父库存。
                foreach(var owned in made.AsEnumerable().Reverse())if(!owned.Retired)try{
                    if(owned.Item.parentInventory is {} parent&&!parent.Expel(owned.Item))throw new InvalidOperationException("fixture expel refused");
                    owned.Retired=true;owned.Item.Destroy();
                }catch(Exception error){check(false,mode+": cleanup "+error.Message);}
            }
        }
    }
    private static GameGridInventory Grid(GameItem root)=>root.contentWindow.childElement.TryCast<GameGridInventory>()??throw new InvalidOperationException("nested fixture grid absent");
    private static void Put(GameInventory grid,GameItem item){var marker=grid.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(item.unitCount)!=item.unitCount)throw new InvalidOperationException("graph fixture admission: "+grid.identifier);}
    private sealed class RefuseVerify:IMachineBatchTransaction
    {
        private readonly DefaultMachineTransaction inner;internal RefuseVerify(DefaultMachineTransaction inner)=>this.inner=inner;
        public bool Validate()=>inner.Validate();public void Apply(IReadOnlyList<GameItem> products)=>inner.Apply(products);
        public bool Verify()=>false;public bool Restore()=>inner.Restore();public void Fault(string reason)=>inner.Fault(reason);
    }
}
