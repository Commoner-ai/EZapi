using Il2Cpp;
namespace EZapi;

/// <summary>原生移除/槽位接纳的短期冻结。不是存档事务，也不阻止Destroy、战斗或恶意Mod直接改字段。</summary>
public static class InventoryFreezeApi
{
    private sealed record Entry(int InstanceId,string ItemId,string Group,int Count);
    private sealed class Lease:IDisposable
    {
        private int _disposed;
        internal bool Active=>Volatile.Read(ref _disposed)==0;
        internal readonly long Epoch,Generation;internal readonly string Group;internal readonly IntPtr[] Items;
        internal Lease(long epoch,long generation,string group,IntPtr[] items)=>(Epoch,Generation,Group,Items)=(epoch,generation,group,items);
        public void Dispose() {
            // 只修改托管表，可从清理/异常路径重复调用。旧Epoch的句柄不能解开新周目的冻结。
            if(Interlocked.Exchange(ref _disposed,1)!=0)return;
            lock(Sync) {
                if(_epoch!=Epoch||_generation!=Generation)return;
                foreach(var pointer in Items)if(Frozen.TryGetValue(pointer,out var entry)&&entry.Group==Group) {
                    if(entry.Count==1)Frozen.Remove(pointer);else Frozen[pointer]=entry with {Count=entry.Count-1};
                }
                _leases--;
            }
        }
    }
    private static readonly object Sync=new();
    private static readonly Dictionary<IntPtr,Entry> Frozen=new();
    private static long _epoch=-1,_serial,_generation;
    private static int _leases;
    // 一次内部原生调用的精确许可，不让公开IsFrozen伪装成未冻结，也不按全局group绕过。
    private sealed class Permit
    {
        internal readonly Lease Lease;internal readonly GameItem Item;internal readonly IntPtr Pointer,Source,Destination;
        internal readonly int Instance;internal readonly string Id;internal readonly int Amount;internal readonly GameRunIdentity Run;
        internal IntPtr Marker,Target;internal bool AcceptUsed,ExpelUsed,Armed;internal int Queries;
        internal Permit(Lease lease,GameItem item,GameInventory? source,GameInventory? destination,int amount)
        {Lease=lease;Run=NativeAccess.RequireRun();Item=item;Pointer=item.Pointer;Instance=item.uniqueId;Id=item.identifier;Source=source?.Pointer??IntPtr.Zero;Destination=destination?.Pointer??IntPtr.Zero;Amount=amount;}
        internal bool Current()=>Lease.Active&&Lease.Epoch==_epoch&&Lease.Generation==_generation&&GameSession.OnThread&&
            NativeAccess.SameRun(Run)&&Item.Pointer==Pointer&&Item.uniqueId==Instance&&Item.identifier==Id;
    }
    private static Permit? _permit;
    public static bool IsAvailable => GameSession.OnThread && InventoryRuntime.VerifyFreeze();

    /// <summary>检查当前实例是否被冻结；不是按物品ID冻结所有同类物品。</summary>
    public static bool IsFrozen(GameItem item)
    {
        NativeAccess.RequireItem(item);return IsAvailable&&Matches(item);
    }
    internal static bool Matches(GameItem? item)
    {
        if(!InventoryRuntime.FreezeReady || !GameSession.OnThread || !GameSession.TryResolve(null,out _) || item is null || item.Pointer==IntPtr.Zero)return false;
        lock(Sync) {
            if(_epoch!=GameSession.Identity!.Epoch) {ResetCore();return false;}
            return Frozen.TryGetValue(item.Pointer,out var entry)&&entry.InstanceId==item.uniqueId&&entry.ItemId==item.identifier;
        }
    }
    /// <summary>整组原子认领，包含容器已有后代。不同group冲突返回null，同group可嵌套计数；null group每次独立。</summary>
    public static IDisposable? TryAcquire(IEnumerable<GameItem> items,string? group=null)
    {
        var run=NativeAccess.RequireRun();
        ArgumentNullException.ThrowIfNull(items);
        if(group is not null && (string.IsNullOrWhiteSpace(group)||group.Length>160||group.Any(char.IsControl)))
            throw new ArgumentException("EZAPI_FREEZE_GROUP: readable group <=160 required.");
        if(!IsAvailable || InventoryApi.IsItemDragActive)return null;
        var nodes=new Queue<GraphNodeStorage>();var visited=new HashSet<IntPtr>();var records=new Dictionary<IntPtr,Entry>();
        var effectiveGroup=group??("ezapi.freeze."+Interlocked.Increment(ref _serial));
        var roots=0;var edges=0;
        foreach(var item in items) {
            if(++roots>4096)throw new ArgumentException("EZAPI_FREEZE_LIMIT: too many roots.");
            NativeAccess.RequireItem(item);
            nodes.Enqueue(item.TryCast<GraphNodeStorage>()??throw new ArgumentException("EZAPI_FREEZE_GRAPH: no native graph."));
        }
        if(roots==0)throw new ArgumentException("EZAPI_FREEZE_INPUT: nonempty item set required.");
        while(nodes.Count>0) {
            var node=nodes.Dequeue();
            if(node is null||node.Pointer==IntPtr.Zero)throw new ArgumentException("EZAPI_FREEZE_GRAPH: dead graph node.");
            if(!visited.Add(node.Pointer))continue;
            if(visited.Count>8192)throw new ArgumentException("EZAPI_FREEZE_LIMIT: graph too large.");
            var item=node.TryCast<GameItem>();
            if(item is not null) {
                NativeAccess.RequireItem(item);
                var parent=item.parentInventory;
                if(parent is not null && parent.TryCast<GameGridInventory>() is null && parent.TryCast<GameGridScrollableInventory>() is null && parent.TryCast<GameSlotInventory>() is null)
                    return null; // 没有原生Expel钩子的库存不能假装已锁住。
                if(records.Count>=4096)throw new ArgumentException("EZAPI_FREEZE_LIMIT: too many items.");
                records.Add(item.Pointer,new(item.uniqueId,item.identifier,effectiveGroup,1));
            }
            var children=node.children;if(children is null)continue;
            if(children.Count>8192-edges)throw new ArgumentException("EZAPI_FREEZE_LIMIT: too many graph edges.");
            edges+=children.Count;foreach(var child in children)nodes.Enqueue(child);
        }
        if(!NativeAccess.SameRun(run))return null;
        lock(Sync) {
            if(_epoch!=run.Epoch){ResetCore();_epoch=run.Epoch;}
            if(_leases>=256||Frozen.Keys.Union(records.Keys).Count()>4096)return null;
            foreach(var pair in records)if(Frozen.TryGetValue(pair.Key,out var old) &&
                (old.InstanceId!=pair.Value.InstanceId || old.ItemId!=pair.Value.ItemId || old.Group!=effectiveGroup))return null;
            foreach(var pair in records)Frozen[pair.Key]=Frozen.TryGetValue(pair.Key,out var old)?old with {Count=old.Count+1}:pair.Value;
            _leases++;return new Lease(run.Epoch,_generation,effectiveGroup,records.Keys.ToArray());
        }
    }
    internal static IDisposable? Extend(IDisposable handle,IEnumerable<GameItem> items)
    {var lease=Owned(handle);return TryAcquire(items,lease.Group);}
    private static Lease Owned(IDisposable handle)
    {
        var run=NativeAccess.RequireRun();
        if(handle is not Lease lease||!lease.Active||lease.Epoch!=run.Epoch||lease.Epoch!=_epoch||lease.Generation!=_generation)
            throw new InvalidOperationException("EZAPI_FREEZE_PERMIT: current owned lease required.");
        return lease;
    }
    private static bool Owns(Lease lease,GameItem? item)
    {
        if(item is null||!Matches(item))return true;
        lock(Sync)return Frozen.TryGetValue(item.Pointer,out var entry)&&entry.Group==lease.Group;
    }
    /// <summary>仅内部资源事务：解析一次槽位、接纳一次；可查询/移出精确源实例，不能操作其它冻物品。</summary>
    internal static int Transfer(IDisposable handle,GameItem item,GameInventory destination,int amount,Func<SlotMarker?> resolve)
    {
        var lease=Owned(handle);NativeAccess.RequireItem(item);
        if(_permit is not null||amount<1||item.unitCount<amount||!IsAvailable||!Owns(lease,item)||
            !Owns(lease,item.parentInventory?.GetParentItem())||!Owns(lease,destination.GetParentItem()))
            throw new InvalidOperationException("EZAPI_FREEZE_PERMIT: reentry, unavailable hooks or foreign freeze.");
        var permit=new Permit(lease,item,item.parentInventory,destination,amount);_permit=permit;
        try {
            // 许可只解除本框架冻结，原生移除资格与数量上限仍必须成立。
            // 新产物尚游离，没有可移出的源库存；原生MayRemove此时为false，不能误拦首次交付。
            if(permit.Source!=IntPtr.Zero&&(!item.MayRemove()||item.MaxNumRemove()<amount))return 0;
            var marker=resolve();
            if(!permit.Current()||marker is null||marker.item?.Pointer!=item.Pointer||marker.inventory?.Pointer!=destination.Pointer||
                (item.parentInventory?.Pointer??IntPtr.Zero)!=permit.Source||item.unitCount<amount||
                marker.numTransfer<amount||!Owns(lease,marker.targetItem)||!marker.IsValid())return 0;
            permit.Marker=marker.Pointer;permit.Target=marker.targetItem?.Pointer??IntPtr.Zero;permit.Armed=true;
            return marker.TryAcceptOnce(amount);
        }finally{if(ReferenceEquals(_permit,permit))_permit=null;}
    }
    internal static bool Expel(IDisposable handle,GameItem item,GameInventory inventory)
    {
        var lease=Owned(handle);NativeAccess.RequireItem(item);
        if(_permit is not null||item.parentInventory?.Pointer!=inventory.Pointer||!IsAvailable||!Owns(lease,item)||!Owns(lease,inventory.GetParentItem()))
            throw new InvalidOperationException("EZAPI_FREEZE_PERMIT: invalid expel scope.");
        var permit=new Permit(lease,item,inventory,null,0){Armed=true};_permit=permit;
        try{return inventory.Expel(item);}finally{if(ReferenceEquals(_permit,permit))_permit=null;}
    }
    internal static bool AllowsQuery(GameItem item)
    {
        var p=_permit;return p is not null&&p.Current()&&p.Pointer==item.Pointer&&p.Instance==item.uniqueId&&++p.Queries<=32;
    }
    internal static bool AllowsExpel(GameInventory inventory,GameItem item)
    {
        var p=_permit;
        if(p is null||!p.Armed||p.ExpelUsed||!p.Current()||inventory.Pointer!=p.Source||item.Pointer!=p.Pointer)return false;
        p.ExpelUsed=true;return true;
    }
    internal static bool AllowsAccept(SlotMarker marker,int amount)
    {
        var p=_permit;
        if(p is null||!p.Armed||p.AcceptUsed||!p.Current()||amount!=p.Amount||marker.Pointer!=p.Marker||
            marker.item?.Pointer!=p.Pointer||marker.inventory?.Pointer!=p.Destination||(marker.targetItem?.Pointer??IntPtr.Zero)!=p.Target)return false;
        p.AcceptUsed=true;return true;
    }
    // 场景切换可能不改变周目Epoch；独立代数防止旧句柄释放同Epoch的新认领。
    private static void ResetCore(){Frozen.Clear();_leases=0;_epoch=-1;_generation++;_permit=null;}
    internal static void Reset(){lock(Sync)ResetCore();}
    internal static object Report(){lock(Sync)return new {epoch=_epoch,items=Frozen.Count,leases=_leases,permitActive=_permit is not null};}
}
