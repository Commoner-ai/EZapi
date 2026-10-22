using Il2Cpp;
using UnityEngine;
namespace EZapi;

/// <summary>
/// 原版熔炉同步批次的可逆边界。加工算法仍由游戏执行；只延后本批原实例的Destroy。
/// 不跨帧、不改parentInventory、不冻结后全局绕过库存保护。恢复只静默本实例原槽的添加回调。
/// </summary>
internal static class NativeFurnacePower
{
    private static Batch? _active;
    private static readonly List<Batch> Cleanup=new();
    private static long _committed,_restored,_faults;
    private static int[] _lastDestroyedContents=Array.Empty<int>();
    internal static IReadOnlyList<int> LastDestroyedContents=>Array.AsReadOnly(_lastDestroyedContents);
    internal static object Report()=>new{active=_active is not null,pendingCleanups=Cleanup.Count,committed=_committed,restored=_restored,recoveryFailed=_faults,lastDestroyedContents=_lastDestroyedContents};
    internal static void Stop()=>Cleanup.Clear();
    // Unity Destroy completes at the end of the frame. Resource settlement stays synchronous;
    // this bounded queue only checks destruction already requested, and never refunds or retries it.
    internal static void Maintain()
    {
        var changed=false;
        foreach(var batch in Cleanup.ToArray()) {
            if(!NativeAccess.SameRun(batch.Run)){Cleanup.Remove(batch);changed=true;continue;}
            if(Time.frameCount-batch.CleanupFrame<2)continue;
            Cleanup.Remove(batch);changed=true;
            try{if(!batch.CleanupComplete())batch.CleanupFault("destruction incomplete after frame boundary");}
            catch(Exception e){batch.CleanupFault("destruction readback: "+e.Message);}
        }
        if(changed)EZapiBootstrap.WriteDiagnostics();
    }
    internal static bool Before(MachineFurnace.__c__DisplayClass0_0 closure,out Batch? batch)
    {
        batch=null;
        if(!GameSession.OnThread)return true;
        try {
            var machine=closure.furnace;var connector=MachineHelper.GetBatterySlot(machine)?.childItem;
            if(ExternalPowerApi.Find(connector) is null)return true;
            if(!ExternalPowerApi.IsAvailable||_active is not null||Cleanup.Count>=128||Cleanup.Any(b=>b.MachinePointer==machine.Pointer)||MachineFaultGuard.Blocked(machine)||InventoryFreezeApi.Matches(machine))return false;
            var input=closure.itemInventoryLeft;
            var output=machine.contentWindow?.childElement?.TryCast<GridPixelElement>()?.GetElement(3,1)?.TryCast<GameInventory>();
            if(input is null||output is null||input.Pointer==output.Pointer||closure.itemInventoryRight?.Pointer!=output.Pointer||
                !GeneralHelper.IsItemOwned(machine)||InventoryApi.IsItemDragActive)return false;
            batch=new Batch(machine,connector!,input,output);_lastDestroyedContents=Array.Empty<int>();_active=batch;return true;
        }catch(Exception e){ExternalPowerRuntime.Record("furnace-preflight",e);return false;}
    }
    internal static bool Draw(GameItem connector,int amount)
    {
        var batch=_active;
        // 零费用的独立查询保留原接口语义；正数只允许精确当前熔炉的一次扣电。
        if(batch is null)return amount==0&&ExternalPowerApi.IsConnected(connector);
        return batch.Debit(connector,amount);
    }
    internal static bool Destroy(GameItemElement item)
    {
        var batch=_active;if(batch is null||batch.Finished||batch.Recovering)return true;
        var snapshot=batch.Items.FirstOrDefault(s=>s.Item.Pointer==item.Pointer);
        if(snapshot is null) {
            // 内装物不由原生熔炉单独消费；提供方/回调提前Destroy只能使本批拒绝，不能破坏可恢复的子图。
            if(batch.Items.Any(s=>s.Contents?.ContainsDescendant(item.Pointer)==true)){batch.Failed=true;return false;}
            return true;
        }
        batch.Deferred.Add(item.Pointer);
        try {
            if(!batch.Current()||!snapshot.Identity())throw Error("deferred instance changed");
            if(item.parentInventory is {} parent) {
                if(parent.Pointer!=snapshot.Parent.Pointer)throw Error("deferred custody changed");
                parent.Expel(item);
            }
            if(item.parentInventory is not null)throw Error("deferred instance could not be detached");
        }catch(Exception e){batch.Failed=true;ExternalPowerRuntime.Record("furnace-defer",e);}
        return false;
    }
    private static InvalidOperationException Error(string reason)=>new("EZAPI_NATIVE_FURNACE: "+reason);

    // 原版加工完成还会更新机器/模块工作标签；失败时与物料一起恢复，避免失败也累计成长。
    private sealed class StateBackup
    {
        private readonly GameItem _item;private readonly NativeInventoryItem _identity;private readonly IntPtr _parent,_state,_dict;
        private readonly NativeTagBackup _tags;private readonly long _value;
        internal StateBackup(GameItem item)
        {
            _item=item;_identity=InventoryChecks.Item(item);_parent=item.parentInventory?.Pointer??IntPtr.Zero;
            _state=item.state.Pointer;_dict=item.state.dict.Pointer;_tags=NativeTagBackup.Capture(item.state);_value=item.unitValue;
        }
        internal bool Matches()=>InventoryChecks.Item(_item)==_identity&&(_item.parentInventory?.Pointer??IntPtr.Zero)==_parent&&
            _item.state.Pointer==_state&&_item.state.dict.Pointer==_dict&&_item.unitValue==_value&&_tags.Matches(_item.state);
        internal bool Restore()
        {
            if(InventoryChecks.Item(_item)!=_identity||(_item.parentInventory?.Pointer??IntPtr.Zero)!=_parent||
                _item.state.Pointer!=_state||_item.state.dict.Pointer!=_dict)return false;
            _tags.Restore(_item.state);_item.SyncModifiedState();_item.unitValue=_value;return Matches();
        }
    }

    internal sealed class ItemState
    {
        internal readonly GameItem Item;internal readonly GameInventory Parent;
        internal readonly NativeInventoryItem Record;internal readonly GridShape Shape;internal readonly int Index;
        internal readonly MachineMaterialGraph? Contents;
        private readonly NativeTagBackup _tags;private readonly IntPtr _state,_dict,_handler;private readonly Vector3? _position;
        private readonly IntPtr _window;
        private readonly long _value;
        internal ItemState(GameItem item,GameInventory parent)
        {
            Item=item;Parent=parent;Record=InventoryChecks.Item(item);_value=item.unitValue;
            _window=item.contentWindow?.Pointer??IntPtr.Zero;Contents=MachineMaterialGraph.Capture(item,observeRoot:false);
            var slot=item.GetIncompleteSlotInInventory();
            if(slot?.item?.Pointer!=item.Pointer||slot.inventory?.Pointer!=parent.Pointer||slot.itemGridShape is null||slot.index<0)
                throw Error("original slot unavailable");
            Shape=slot.itemGridShape.Clone();Index=slot.index;
            _state=item.state.Pointer;_dict=item.state.dict.Pointer;_tags=NativeTagBackup.Capture(item.state);
            var handler=item.TryCast<GameItemElement>()?.handler;_handler=handler?.Pointer??IntPtr.Zero;_position=handler?.transform.localPosition;
        }
        // unitCount may already be zero: RequireItem is deliberately not used during deferred destruction.
        internal bool Identity()=>Item.Pointer.ToInt64()==Record.NativeIdentity&&Item.uniqueId==Record.InstanceId&&Item.identifier==Record.ItemId&&
            Item.state?.Pointer==_state&&Item.state.dict?.Pointer==_dict&&
            (Item.contentWindow?.Pointer??IntPtr.Zero)==_window&&
            (Item.TryCast<GameItemElement>()?.handler?.Pointer??IntPtr.Zero)==_handler;
        internal bool Matches()
        {
            if(!Identity()||Item.parentInventory?.Pointer!=Parent.Pointer||InventoryChecks.Item(Item)!=Record||Item.unitValue!=_value||!_tags.Matches(Item.state))return false;
            var slot=Item.GetIncompleteSlotInInventory();var handler=Item.TryCast<GameItemElement>()?.handler;
            return slot?.index==Index&&slot.itemGridShape is not null&&InventoryChecks.Shape(slot.itemGridShape)==InventoryChecks.Shape(Shape)&&
                (!_position.HasValue||handler is not null&&handler.transform.localPosition.Equals(_position.Value))&&(Contents?.Matches()??true);
        }
        internal bool Restore()
        {
            if(!Identity()||Item.parentInventory is {} parent&&parent.Pointer!=Parent.Pointer)return false;
            _tags.Restore(Item.state);Item.SyncModifiedState();Item.SetUnitCount(Record.Units);Item.unitValue=_value;Item.modifiedShape=Shape.Clone();
            var handler=Item.TryCast<GameItemElement>()?.handler;
            if(_position.HasValue)handler!.transform.localPosition=_position.Value;
            if(Item.parentInventory is null) {
                var may=Parent.mayInventoryAddItemFunc;var add=Parent.onSlotAddItemFunc;var end=Parent.onSlotAddItemEndFunc;
                var late=Parent.onSlotAddItemLateFunc;var itemAdd=Item.onSlotAddItemFunc;var locked=Parent.overrideLockInsert;
                try {
                    Parent.mayInventoryAddItemFunc=null;Parent.onSlotAddItemFunc=null;Parent.onSlotAddItemEndFunc=null;
                    Parent.onSlotAddItemLateFunc=null;Item.onSlotAddItemFunc=null;Parent.overrideLockInsert=false;
                    var slot=Parent.TryInventorySlot(Item,Record.Units,Shape,null);
                    if(slot?.IsValid()!=true||slot.targetItem is not null||slot.item?.Pointer!=Item.Pointer||slot.inventory?.Pointer!=Parent.Pointer||
                        slot.numTransfer<Record.Units||!Parent.UncheckedAccept(Item))return false;
                }finally {
                    Parent.mayInventoryAddItemFunc=may;Parent.onSlotAddItemFunc=add;Parent.onSlotAddItemEndFunc=end;
                    Parent.onSlotAddItemLateFunc=late;Item.onSlotAddItemFunc=itemAdd;Parent.overrideLockInsert=locked;
                }
            }
            // Native admission may rebuild modified shape and render position; require exact original readback.
            return (Contents?.Restore()??true)&&Matches();
        }
    }

    internal sealed class Batch
    {
        private readonly GameItem _machine,_connector;private readonly GameInventory _input,_output;private readonly GameRunIdentity _run;
        private readonly int _machineId,_connectorId,_cost;private readonly IntPtr _inputParent,_outputParent,_connectorParent;
        private readonly NativeInventorySnapshot _beforeInput,_beforeOutput;
        private readonly StateBackup[] _states;
        private readonly IDisposable? _contentsFreeze;
        internal readonly ItemState[] Items;internal readonly HashSet<IntPtr> Deferred=new();
        private ExternalPowerApi.PowerJournal? _power;private bool _drawn,_finishing;
        private readonly List<GameItemElement> _destroyed=new();
        internal GameRunIdentity Run=>_run;
        internal IntPtr MachinePointer=>_machine.Pointer;
        internal int CleanupFrame{get;private set;}
        internal bool CleanupComplete()=>_destroyed.All(i=>i.IsDestroyed());
        internal void CleanupFault(string reason)=>Fault(reason);
        private void DestroyDetached(GameItem item)
        {
            if(item.parentInventory is not null)throw Error("destruction requires detached item");
            var element=item.Cast<GameItemElement>();
            item.Destroy();
            if(item.parentInventory is not null)throw Error("destruction changed custody");
            _destroyed.Add(element);
        }
        internal bool Failed,Finished,Recovering;
        internal Batch(GameItem machine,GameItem connector,GameInventory input,GameInventory output)
        {
            (_machine,_connector,_input,_output,_run)=(machine,connector,input,output,NativeAccess.RequireRun());
            (_machineId,_connectorId,_inputParent,_outputParent,_connectorParent)=(machine.uniqueId,connector.uniqueId,input.parent.Pointer,output.parent.Pointer,connector.parentInventory!.Pointer);
            _beforeInput=InventoryChecks.Read(input,_run);_beforeOutput=InventoryChecks.Read(output,_run);
            if(input.childItems.Count+output.childItems.Count>128)throw Error("max128 input/output stacks");
            Items=input.childItems.ToArray().Select(i=>new ItemState(i,input)).Concat(output.childItems.ToArray().Select(i=>new ItemState(i,output))).ToArray();
            if(Items.Select(s=>s.Record.InstanceId).Distinct().Count()!=Items.Length)throw Error("duplicate input/output instance");
            if(Items.Sum(s=>s.Contents?.ItemCount??1)>4096)throw Error("max4096 total furnace material/output graph items");
            var provider=ExternalPowerApi.Find(connector)??throw Error("connector provider disappeared");
            foreach(var item in Items.Where(s=>s.Parent.Pointer==input.Pointer&&s.Contents is not null)) {
                if(provider.FurnaceMaterials!=MachineMaterialContentsPolicy.ConsumeGraph)throw Error("nested input requires explicit ConsumeGraph material policy");
                if(!item.Contents!.CanConsume())throw Error("nested input graph is not removable or owned");
            }
            var descendants=Items.SelectMany(s=>s.Contents?.Descendants??Array.Empty<GameItem>()).ToArray();
            if(descendants.Select(i=>i.Pointer).Distinct().Count()!=descendants.Length)throw Error("shared furnace material/output descendants");
            var modules=MachineHelper.GetModules(machine).ToArray();if(modules.Length>128)throw Error("max128 modules");
            _states=new[]{machine,connector}.Concat(modules).DistinctBy(i=>i.Pointer).Select(i=>new StateBackup(i)).ToArray();
            _cost=Power.GetMachineCost(machine);
            // 原版零费路径可能不调用Draw；日记仅保存只读能源基线，不创建作者事务。
            _contentsFreeze=descendants.Length==0?null:InventoryFreezeApi.TryAcquire(descendants)??throw Error("furnace contents already frozen or hooks unavailable");
            try{if(_cost==0)_power=ExternalPowerApi.Open(machine,connector,0);}catch{_contentsFreeze?.Dispose();throw;}
        }
        internal bool Current()=>NativeAccess.SameRun(_run)&&_machine.uniqueId==_machineId&&_connector.uniqueId==_connectorId&&
            _input.parent?.Pointer==_inputParent&&_output.parent?.Pointer==_outputParent&&_connector.parentInventory?.Pointer==_connectorParent&&
            MachineHelper.GetBatterySlot(_machine)?.childItem?.Pointer==_connector.Pointer;
        internal bool Debit(GameItem connector,int amount)
        {
            if(Finished||Recovering||_drawn||connector.Pointer!=_connector.Pointer||amount<0||amount!=_cost||!Current()){Failed=true;return false;}
            _drawn=true;
            try {
                _power??=ExternalPowerApi.Open(_machine,_connector,amount);
                if(!_power.Validate())throw Error("provider validation rejected");
                _power.Debit();return _power.Verify();
            }catch(Exception e){Failed=true;ExternalPowerRuntime.Record("furnace-debit",e);return false;}
        }
        internal void Finish(bool aborted)
        {
            if(Finished||_finishing)return;_finishing=true;
            var success=false;var recovered=true;var failures=new List<string>();
            void Attempt(string stage,Func<bool> action){try{if(!action()){recovered=false;failures.Add(stage);}}catch(Exception e){recovered=false;failures.Add(stage+": "+e.Message);}}
            try {
                try {
                    if(!Current())throw Error("run or machine custody changed");
                    var same=InventoryChecks.Equal(_beforeInput,InventoryChecks.Read(_input,_run))&&InventoryChecks.Equal(_beforeOutput,InventoryChecks.Read(_output,_run))&&Items.All(s=>s.Matches())&&_states.All(s=>s.Matches());
                    // A native no-op without debit is valid but is not a committed production batch.
                    if(!aborted&&!Failed&&!_drawn&&same)return;
                    success=!aborted&&!Failed&&(_drawn||_cost==0)&&_power?.Verify()==true&&
                        Items.All(s=>s.Contents?.Matches()??true)&&
                        !Items.Any(s=>s.Parent.Pointer==_output.Pointer&&Deferred.Contains(s.Item.Pointer))&&
                        _input.childItems.ToArray().Sum(i=>(long)i.unitCount)<_beforeInput.Items.Sum(i=>(long)i.Units)&&
                        _output.childItems.ToArray().Sum(i=>(long)i.unitCount)>_beforeOutput.Items.Sum(i=>(long)i.Units);
                }catch(Exception e){failures.Add("final readback: "+e.Message);}
                if(!success) {
                    Recovering=true;
                    Attempt("power",()=>_power is null||_power.Restore());
                    // Only newly attached products in the two captured inventories; unknown moved objects are never deleted.
                    var originals=Items.Select(s=>s.Item.Pointer).ToHashSet();
                    foreach(var inv in new[]{_input,_output})Attempt("new products",()=>{
                        if(!Current())return false;
                        var ok=true;
                        foreach(var item in inv.childItems.ToArray().Where(i=>!originals.Contains(i.Pointer))) {
                            try{inv.Expel(item);if(item.parentInventory is not null){ok=false;continue;}DestroyDetached(item);}catch{ok=false;}
                        }
                        return ok;
                    });
                    foreach(var snapshot in Items)Attempt("item "+snapshot.Record.InstanceId,()=>Current()&&snapshot.Restore());
                    foreach(var state in _states)Attempt("machine/module state",()=>Current()&&state.Restore());
                    Attempt("inventory readback",()=>Current()&&InventoryChecks.Equal(_beforeInput,InventoryChecks.Read(_input,_run))&&
                        InventoryChecks.Equal(_beforeOutput,InventoryChecks.Read(_output,_run))&&Items.All(s=>s.Matches())&&_states.All(s=>s.Matches())&&(_power is null||_power.IsRestored));
                    if(recovered)_restored++;
                }else {
                    _committed++;
                    // Commit is irreversible from here; cleanup failure quarantines, never refunds successful production.
                    Recovering=true;
                    foreach(var snapshot in Items.Where(s=>Deferred.Contains(s.Item.Pointer)))Attempt("committed destruction",()=>{
                        if(!Current()||!snapshot.Identity()||snapshot.Item.parentInventory is not null)return false;
                        if(snapshot.Contents is {ItemCount:>1} contents){
                            try{contents.ConsumeContents(_contentsFreeze??throw Error("contents freeze missing"),DestroyDetached);}
                            finally{_lastDestroyedContents=_lastDestroyedContents.Concat(contents.DestroyedInstanceIds).ToArray();}
                        }
                        DestroyDetached(snapshot.Item);return true;
                    });
                }
                if(!recovered||failures.Count>0&&!success)Fault(string.Join("; ",failures));
            }finally {
                Finished=true;_finishing=false;Recovering=false;
                if(ReferenceEquals(_active,this))_active=null;
                _contentsFreeze?.Dispose();
                _power?.Dispose();
                if(_destroyed.Count>0){CleanupFrame=Time.frameCount;Cleanup.Add(this);}
            }
        }
        private void Fault(string reason)
        {
            _faults++;_power?.Fault(reason);
            if(NativeAccess.SameRun(_run))MachineFaultGuard.Record(_machine,new(MachineTransactionStatus.RecoveryFailed,Array.Empty<MachineProductReceipt>(),reason,new[]{reason}));
            ExternalPowerRuntime.Record("furnace-recovery",Error(reason));
        }
    }
}
