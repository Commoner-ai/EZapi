using Il2Cpp;
namespace EZapi;

/// <summary>
/// 默认批次的临时保管图。只用原生TryAcceptOnce拆堆/转移/合堆，不直接写数量和父库存。
/// 验证前保持全部材料可逆；提交确认后才逐件Expel/Destroy。本类不持久化或跨帧重试。
/// </summary>
internal sealed class MachineMaterialEscrow
{
    private sealed class Entry
    {
        internal readonly GameItem Original;internal readonly GameInventory Source;internal readonly int Amount;
        internal readonly GridShape Shape;internal readonly NativeInventoryItem Identity;internal readonly NativeTagBackup Tags;internal readonly long Value;
        internal readonly IntPtr State,Dict;
        internal readonly MachineMaterialGraph? Graph;
        internal GameGridInventory? Grid;internal NativeInventorySnapshot? BeforeSource,BeforeTarget;
        internal GameItem? Staged;internal NativeInventoryItem? StagedIdentity;internal int Moved;internal bool Attempted,Assessed,Retired;
        internal Entry(MachineItemTake take,MachineMaterialContentsPolicy contents) {
            Original=take.Item;Source=Original.parentInventory??throw Error("input must belong to inventory");Amount=take.Count;
            Shape=Original.modifiedShape.Clone();Identity=InventoryChecks.Item(Original);Value=Original.unitValue;
            State=Original.state.Pointer;Dict=Original.state.dict.Pointer;Tags=NativeTagBackup.Capture(Original.state);
            if(Amount<1||Amount>Identity.Units)throw Error("invalid material take");
            Graph=MachineMaterialGraph.Capture(Original);
            if(Graph is not null&&(contents!=MachineMaterialContentsPolicy.ConsumeGraph||Amount!=Identity.Units))throw Error("nested material requires explicit ConsumeGraph and whole root stack");
        }
    }
    private readonly GameRunIdentity _run;private readonly Entry[] _entries;private readonly List<IDisposable> _leases=new();
    private GameItem? _owner;private IDisposable? _freeze;
    private Dictionary<IntPtr,(GameInventory Inventory,NativeInventorySnapshot Snapshot)>? _sources;
    internal IReadOnlyList<int> DestroyedGraphInstanceIds=>Array.AsReadOnly(_entries.SelectMany(e=>e.Graph?.DestroyedInstanceIds??Array.Empty<int>()).ToArray());
    internal MachineMaterialEscrow(MachineBatchContext context,MachineMaterialContentsPolicy contents=MachineMaterialContentsPolicy.Reject)
    {
        _run=NativeAccess.RequireRun();
        if(context.Items.Count>64||context.Items.Select(t=>t.Item.Pointer).Distinct().Count()!=context.Items.Count)throw Error("max64 unique input stacks");
        _entries=context.Items.Select(t=>new Entry(t,contents)).ToArray();
        if(_entries.Sum(e=>e.Graph?.ItemCount??1)>4096)throw Error("max4096 total material graph items");
    }
    internal bool Validate()=>NativeAccess.SameRun(_run)&&_entries.All(e=>e.Original.Pointer.ToInt64()==e.Identity.NativeIdentity&&
        e.Original.uniqueId==e.Identity.InstanceId&&e.Original.identifier==e.Identity.ItemId&&e.Original.unitCount==e.Identity.Units&&
        e.Original.parentInventory?.Pointer==e.Source.Pointer&&e.Original.state.Pointer==e.State&&e.Original.state.dict.Pointer==e.Dict&&e.Original.unitValue==e.Value&&(e.Graph?.CanConsume()??true));
    internal void Begin(GameItem machine,IReadOnlyList<GameItem> products)
    {
        if(_owner is not null||!Validate())throw Error("stale or reused material journal");
        _owner=ItemDirectory.CreateEmptyItem("ezapi.internal.material_escrow");GeneralHelper.SetItemOwned(_owner,true);
        var window=new PixelWindow(false,"EZapi内部材料保管");_owner.SetContentWindow(window);
        var root=new GridPixelElement(1,Math.Max(1,_entries.Length),false);
        if(!window.Attach(root.Cast<PixelElement>()))throw Error("escrow root attach failed");
        for(var i=0;i<_entries.Length;i++) {
            var grid=new GameGridInventory(64,64){identifier="ezapi.internal.material_escrow."+i};_entries[i].Grid=grid;
            if(!root.AttachPos(grid.Cast<PixelElement>(),0,i))throw Error("escrow grid attach failed");
        }
        _freeze=InventoryFreezeApi.TryAcquire(new[]{machine,_owner}.Concat(products))??throw Error("cannot acquire machine/product/escrow freeze");
        _leases.Add(_freeze);
    }
    internal void Stage()
    {
        var freeze=_freeze??throw Error("freeze missing");
        foreach(var e in _entries) {
            if(!NativeAccess.SameRun(_run))throw Error("run changed before material transfer");
            e.BeforeSource=InventoryChecks.Read(e.Source,_run);e.BeforeTarget=InventoryChecks.Read(e.Grid!,_run);e.Attempted=true;
            var reported=-1;
            try {reported=InventoryFreezeApi.Transfer(freeze,e.Original,e.Grid!,e.Amount,()=>e.Grid!.TryFindOneValidInventorySlot(e.Original,false));}
            finally {Assess(e);}
            if(!e.Assessed||reported!=e.Amount||e.Moved!=e.Amount)throw Error("native material transfer/readback failed");
            if(e.Graph is not null&&(e.Staged!.Pointer!=e.Original.Pointer||!e.Graph.Matches()))throw Error("nested graph changed during native transfer");
            var extra=InventoryFreezeApi.Extend(freeze,new[]{e.Staged!})??throw Error("cannot freeze staged split");_leases.Add(extra);
        }
        _sources=_entries.Select(e=>e.Source).DistinctBy(i=>i.Pointer).ToDictionary(i=>i.Pointer,i=>(i,InventoryChecks.Read(i,_run)));
    }
    private void Assess(Entry e)
    {
        if(!NativeAccess.SameRun(_run))return;
        var source=InventoryChecks.Read(e.Source,_run);var target=InventoryChecks.Read(e.Grid!,_run);
        var audit=InventoryTransferAudit.Check(e.BeforeSource!,e.BeforeTarget!,source,target,e.Identity,e.Amount);
        if(!audit.Valid||audit.Moved>0&&target.Items.Count!=1)return;
        e.Moved=audit.Moved;e.Assessed=true;
        if(e.Moved>0) {
            e.Staged=e.Grid!.childItems[0];e.StagedIdentity=target.Items[0];
            if(e.Staged.identifier!=e.Identity.ItemId||e.Staged.unitCount!=e.Moved){e.Assessed=false;return;}
        }
    }
    internal bool Verify()=>NativeAccess.SameRun(_run)&&_sources is not null&&_sources.Values.All(s=>InventoryChecks.Equal(s.Snapshot,InventoryChecks.Read(s.Inventory,_run)))&&_entries.All(e=>e.Assessed&&e.Moved==e.Amount&&!e.Retired&&e.Staged is not null&&
        e.Grid!.childItems.Count==1&&InventoryChecks.Item(e.Staged)==e.StagedIdentity&&e.Staged.parentInventory?.Pointer==e.Grid.Pointer&&(e.Graph?.Matches()??true));
    internal bool Restore()
    {
        var ok=NativeAccess.SameRun(_run);
        try {
            if(!ok)return false;
            foreach(var e in _entries.Reverse()) {
                try {
                    if(!e.Attempted)continue;
                    if(!e.Assessed){Assess(e);if(!e.Assessed){ok=false;continue;}}
                    if(e.Moved==0)continue;
                    var staged=e.Staged!;var beforeA=InventoryChecks.Read(e.Grid!,_run);var beforeB=InventoryChecks.Read(e.Source,_run);
                    var original=e.StagedIdentity!;
                    if(e.Retired||InventoryChecks.Item(staged)!=original)throw Error("staged material identity changed");
                    var reported=InventoryFreezeApi.Transfer(_freeze!,staged,e.Source,e.Moved,()=>
                        e.Moved<e.Identity.Units?e.Original.GetStackSlot(staged):e.Source.TryInventorySlot(staged,e.Moved,e.Shape,null));
                    // 合堆可能销毁staged：之后只读两个库存，不再解引用该包装对象。
                    var afterA=InventoryChecks.Read(e.Grid!,_run);var afterB=InventoryChecks.Read(e.Source,_run);
                    var audit=InventoryTransferAudit.Check(beforeA,beforeB,afterA,afterB,original,e.Moved);
                    if(!audit.Valid||audit.Moved!=e.Moved||reported!=e.Moved||afterA.Items.Count!=0)throw Error("material return readback failed");
                    e.Retired=true;
                    if(e.Original.state.Pointer!=e.State||e.Original.state.dict.Pointer!=e.Dict)throw Error("original material state replaced");
                    e.Tags.Restore(e.Original.state);e.Original.SyncModifiedState();e.Original.unitValue=e.Value;
                    if(e.Graph?.Restore()==false)throw Error("nested material graph restoration failed");
                    if(InventoryChecks.Item(e.Original)!=e.Identity)throw Error("original material quantity/shape identity differs after restore");
                }catch{ok=false;}
            }
            if(_owner is not null&&_entries.All(e=>e.Grid is null||e.Grid.childItems.Count==0)) {_owner.Destroy();_owner=null;}
            else if(_owner is not null)ok=false; // 未退回资源保留在保管图，不能Destroy冒充补偿成功。
            return ok;
        }finally{Release();}
    }
    internal void Consume()
    {
        var failures=new List<string>();
        try {
            if(!Verify())throw Error("commit material journal no longer matches");
            foreach(var e in _entries) {
                try {
                    if(e.Graph is not null){e.Retired=true;e.Graph.Consume(_freeze!);continue;}
                    if(!InventoryFreezeApi.Expel(_freeze!,e.Staged!,e.Grid!))throw Error("consumed material expel failed");
                    // 已确认提交，材料不再退回；先标记，Destroy抛错时也不可重复销毁同实例。
                    e.Retired=true;e.Staged!.Destroy();
                }catch(Exception ex){failures.Add(e.Identity.InstanceId+": "+ex.Message);}
            }
            if(_owner is not null&&_entries.All(e=>e.Grid is null||e.Grid.childItems.Count==0)) {_owner.Destroy();_owner=null;}
            else failures.Add("escrow still contains consumed materials; retained");
        }finally{Release();}
        if(failures.Count>0)throw Error("committed cleanup: "+string.Join("; ",failures));
    }
    internal int Deliver(GameItem product,GameInventory output,GridShape shape)
        =>InventoryFreezeApi.Transfer(_freeze!,product,output,1,()=>output.TryInventorySlot(product,1,shape,null));
    internal bool ExpelProduct(GameItem product)=>product.parentInventory is not {} parent||InventoryFreezeApi.Expel(_freeze!,product,parent);
    internal void Abandon()=>Release(); // 生命周期改变时只释放托管冻结，不触碰旧原生引用。
    private void Release(){foreach(var lease in _leases.AsEnumerable().Reverse())lease.Dispose();_leases.Clear();_freeze=null;}
    private static InvalidOperationException Error(string reason)=>new("EZAPI_MACHINE_MATERIAL: "+reason);
}
