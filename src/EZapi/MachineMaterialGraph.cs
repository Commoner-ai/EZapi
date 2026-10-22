using Il2Cpp;
namespace EZapi;

/// <summary>
/// 一次同步批次中的材料后代日记。移动根材料时不重建内装物；审计所有节点边、库存、身份、标签与价值。
/// 保留原生对象仅至本次调用结束；提交后按原图拓扑逆序逐件Expel/Destroy，不依赖根Destroy是否级联。
/// </summary>
internal sealed class MachineMaterialGraph
{
    private sealed class ItemState
    {
        internal readonly GameItem Item;internal readonly NativeInventoryItem Identity;internal readonly NativeTagBackup Tags;
        internal readonly IntPtr Parent,State,Dict,Window;internal readonly long Value;internal readonly bool Root,TrackPlacement;
        internal ItemState(GameItem item,bool root,bool trackRootPlacement) {
            Item=item;Root=root;TrackPlacement=!root||trackRootPlacement;Identity=InventoryChecks.Item(item,includeGeometry:TrackPlacement);Parent=item.parentInventory?.Pointer??IntPtr.Zero;
            State=item.state.Pointer;Dict=item.state.dict.Pointer;Window=item.contentWindow?.Pointer??IntPtr.Zero;
            Tags=NativeTagBackup.Capture(item.state);Value=item.unitValue;
        }
        internal bool IdentityMatches(bool metadata) {
            var actual=InventoryChecks.Item(Item,includeGeometry:TrackPlacement);
            return (metadata?actual:actual with {PlayerOwned=Identity.PlayerOwned})==Identity&&
                (!TrackPlacement||(Item.parentInventory?.Pointer??IntPtr.Zero)==Parent)&&Item.state.Pointer==State&&Item.state.dict.Pointer==Dict&&
                (Item.contentWindow?.Pointer??IntPtr.Zero)==Window&&(!metadata||Item.unitValue==Value&&Tags.Matches(Item.state));
        }
        internal void Restore(){Tags.Restore(Item.state);Item.SyncModifiedState();Item.unitValue=Value;}
    }
    private sealed record Node(GraphNodeStorage Storage,IntPtr Pointer,IntPtr[] Children);
    private readonly GameRunIdentity _run;private readonly GameItem _root;private readonly ItemState[] _items;
    // 原版熔炉会把根数量减到0并延后Destroy；此模式只观察根的子图，根本身由熔炉槽位日记恢复。
    private readonly bool _observeRoot;
    private readonly IntPtr _rootWindow;
    private readonly Node[] _nodes;private readonly (GameInventory Inventory,NativeInventorySnapshot Snapshot)[] _inventories;
    internal int ItemCount=>_items.Length;
    private readonly List<int> _destroyed=new();
    internal IReadOnlyList<int> DestroyedInstanceIds=>_destroyed.AsReadOnly();
    private MachineMaterialGraph(GameItem root,IReadOnlyList<GameItem> graph,bool observeRoot,bool trackRootPlacement=false)
    {
        _run=NativeAccess.RequireRun();_root=root;_observeRoot=observeRoot;_rootWindow=root.contentWindow?.Pointer??IntPtr.Zero;
        var queue=new Queue<GraphNodeStorage>();var seen=new HashSet<IntPtr>();var nodes=new List<Node>();var inventories=new List<(GameInventory,NativeInventorySnapshot)>();
        queue.Enqueue(root.Cast<GraphNodeStorage>());var edges=0;
        while(queue.Count>0) {
            var node=queue.Dequeue();if(!seen.Add(node.Pointer))continue;
            if(seen.Count>8192)throw Error("max8192 nodes");
            var children=node.children?.ToArray()??Array.Empty<GraphNodeStorage>();edges+=children.Length;if(edges>8192)throw Error("max8192 edges");
            nodes.Add(new(node,node.Pointer,children.Select(c=>c.Pointer).OrderBy(p=>p.ToInt64()).ToArray()));
            foreach(var child in children)queue.Enqueue(child);
            if(node.TryCast<GameInventory>() is {} inventory)inventories.Add((inventory,InventoryChecks.Read(inventory,_run)));
        }
        // SaveGraphApi先验证完整图和环；再按拓扑排序，不能把BFS逆序误当任意图的销毁顺序。
        var byId=nodes.ToDictionary(n=>n.Pointer);var degrees=nodes.ToDictionary(n=>n.Pointer,_=>0);
        foreach(var node in nodes)foreach(var child in node.Children)degrees[child]++;
        var ready=new Queue<IntPtr>(degrees.Where(p=>p.Value==0).Select(p=>p.Key));var ordered=new List<GameItem>();var processed=0;
        while(ready.Count>0){var id=ready.Dequeue();processed++;if(byId[id].Storage.TryCast<GameItem>() is {} item)ordered.Add(item);foreach(var child in byId[id].Children)if(--degrees[child]==0)ready.Enqueue(child);}
        if(processed!=nodes.Count||ordered.Count!=graph.Count||!ordered.Select(i=>i.Pointer).ToHashSet().SetEquals(graph.Select(i=>i.Pointer)))throw Error("invalid material graph topology");
        if(ordered.Sum(i=>(long)i.state.dict.Count)>16384)throw Error("max16384 tags per material graph");
        _nodes=nodes.ToArray();_inventories=inventories.ToArray();_items=ordered.Select(i=>new ItemState(i,i.Pointer==root.Pointer,trackRootPlacement)).ToArray();
        if(_items.Any(i=>!i.Root&&!seen.Contains(i.Parent)))throw Error("descendant parent inventory is outside material graph");
    }
    internal static MachineMaterialGraph? Capture(GameItem root)=>Capture(root,true);
    /// <summary>只读回调审计：即使无内装物也捕获根标签，并严格检查根的位置与形状；不用于材料托管搬移。</summary>
    internal static MachineMaterialGraph CaptureState(GameItem root)=>new(root,SaveGraphApi.ItemsForObservation(root),true,trackRootPlacement:true);
    internal static MachineMaterialGraph? Capture(GameItem root,bool observeRoot)
    {
        var graph=SaveGraphApi.Items(root);return graph.Count==1&&(observeRoot||root.contentWindow is null)?null:new(root,graph,observeRoot);
    }
    internal IReadOnlyList<GameItem> Descendants=>Array.AsReadOnly(_items.Where(i=>!i.Root).Select(i=>i.Item).ToArray());
    internal bool ContainsDescendant(IntPtr pointer)=>_items.Any(i=>!i.Root&&i.Identity.NativeIdentity==pointer.ToInt64());
    internal bool Matches(bool metadata=true)=>NativeAccess.SameRun(_run)&&(_root.contentWindow?.Pointer??IntPtr.Zero)==_rootWindow&&_items.Where(i=>_observeRoot||!i.Root).All(i=>i.IdentityMatches(metadata))&&
        _nodes.All(n=>n.Storage.Pointer==n.Pointer&&(n.Storage.children?.ToArray().Select(c=>c.Pointer).OrderBy(p=>p.ToInt64()).ToArray()??Array.Empty<IntPtr>()).SequenceEqual(n.Children))&&
        (!metadata||_inventories.All(p=>InventoryChecks.Equal(p.Snapshot,InventoryChecks.Read(p.Inventory,_run))));
    internal bool CanConsume()=>Matches()&&_items.All(i=>GeneralHelper.IsItemOwned(i.Item)&&!InventoryFreezeApi.IsFrozen(i.Item)&&i.Item.MayRemove()&&i.Item.MaxNumRemove()>=i.Item.unitCount);
    internal bool Restore()
    {
        if(!Matches(metadata:false))return false;
        foreach(var item in _items.Where(i=>_observeRoot||!i.Root))item.Restore();return Matches();
    }
    /// <summary>已确认原版熔炉提交后，仅清理子图；根允许数量0且已游离，调用者最后自行销毁根。
    /// 每步只读取尚未销毁的节点，保留纯托管收据；拒绝新增节点、身份/库存变化或跨周目调用。</summary>
    internal void ConsumeContents(IDisposable freeze,Action<GameItem> destroy)
    {
        if(_observeRoot||!Matches())throw Error("contents cleanup requires intact furnace contents journal");
        var retired=new HashSet<IntPtr>();var byPointer=_nodes.ToDictionary(n=>n.Pointer);
        bool Remaining()=>NativeAccess.SameRun(_run)&&(_root.contentWindow?.Pointer??IntPtr.Zero)==_rootWindow&&
            _items.Where(i=>!i.Root&&!retired.Contains((IntPtr)i.Identity.NativeIdentity)).All(i=>i.IdentityMatches(true))&&
            _nodes.Where(n=>!retired.Contains(n.Pointer)).All(n=>n.Storage.Pointer==n.Pointer&&
                (n.Storage.children?.ToArray().Select(c=>c.Pointer).OrderBy(p=>p.ToInt64()).ToArray()??Array.Empty<IntPtr>()).SequenceEqual(n.Children.Where(c=>!retired.Contains(c))))&&
            _inventories.Where(p=>!retired.Contains(p.Inventory.Pointer)).All(p=>InventoryChecks.Equal(
                p.Snapshot with {Items=p.Snapshot.Items.Where(i=>!retired.Contains((IntPtr)i.NativeIdentity)).ToArray()},InventoryChecks.Read(p.Inventory,_run)));
        foreach(var entry in _items.Where(i=>!i.Root).Reverse()) {
            if(!Remaining())throw Error("remaining furnace contents changed during committed cleanup");
            var parent=entry.Item.parentInventory??throw Error("furnace content detached before cleanup");
            if(!InventoryFreezeApi.Expel(freeze,entry.Item,parent))throw Error("furnace content expel refused");
            // 记录该物品及其已无存活物品的内部节点；Destroy后绝不再触碰这些包装对象。
            var pending=new Stack<IntPtr>();pending.Push((IntPtr)entry.Identity.NativeIdentity);
            while(pending.Count>0){var p=pending.Pop();if(!retired.Add(p))continue;foreach(var child in byPointer[p].Children)pending.Push(child);}
            destroy(entry.Item);_destroyed.Add(entry.Identity.InstanceId);
        }
        if(!Remaining())throw Error("furnace contents final cleanup readback failed");
    }
    internal void Consume(IDisposable freeze)
    {
        if(!Matches())throw Error("material descendants changed before committed cleanup");
        var remaining=_items.Select(i=>i.Identity.NativeIdentity).ToHashSet();
        foreach(var entry in _items.Reverse()) {
            // 每步仅检查尚未销毁的图，绝不再读取Destroy过的IL2CPP包装对象。
            if(!NativeAccess.SameRun(_run)||!remaining.SetEquals(SaveGraphApi.Items(_root).Select(i=>i.Pointer.ToInt64()))||!entry.IdentityMatches(metadata:true))
                throw Error("remaining graph identity changed during committed cleanup");
            var parent=entry.Item.parentInventory??throw Error("material node detached before cleanup");
            if(!InventoryFreezeApi.Expel(freeze,entry.Item,parent))throw Error("material child expel refused");
            remaining.Remove(entry.Identity.NativeIdentity);entry.Item.Destroy();_destroyed.Add(entry.Identity.InstanceId);
        }
    }
    private static InvalidOperationException Error(string reason)=>new("EZAPI_MACHINE_MATERIAL_GRAPH: "+reason);
}
