using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
namespace EZapi;

/// <summary>
/// 独立物品子图的原生编码/恢复。不会保存游戏、移走源物品或把恢复物品自动放入库存。
/// Decode保留实例ID；原图仍存在时必须拒绝，不能把它当成复制物品入口。
/// </summary>
public static class SaveGraphApi
{
    private static bool _busy;
    private const int MaxBytes=8*1024*1024;

    /// <summary>Whether this game build exposes every required native prerequisite checked before Decode writes objects.</summary>
    public static bool CanDecode=>SaveGraphLoader.Available;
    /// <summary>Reason Decode is unavailable on this build, or null when its required entry point exists.</summary>
    public static string? DecodeUnavailableReason=>SaveGraphLoader.UnavailableReason;

    /// <summary>当前同步调用内的真实子图，含root；上限4096物品/8192节点边。返回的原生对象不跨场景缓存。</summary>
    public static IReadOnlyList<GameItem> Items(GameItem root)=>CaptureItems(root,allowUnassigned:false);

    // 原生CreateEmptyItem的临时实例可能尚未分配存档ID(-1)。只读观察用指针区分这些实例；
    // 公共Items/Encode仍严格拒绝未分配ID，不能把临时观察当作存档可编码的证明。
    internal static IReadOnlyList<GameItem> ItemsForObservation(GameItem root)=>CaptureItems(root,allowUnassigned:true);
    private static IReadOnlyList<GameItem> CaptureItems(GameItem root,bool allowUnassigned)
    {
        var run=NativeAccess.RequireRun();NativeAccess.RequireItem(root);
        var queue=new Queue<GraphNodeStorage>();var visited=new HashSet<IntPtr>();var ids=new HashSet<int>();var result=new List<GameItem>();
        var adjacency=new Dictionary<IntPtr,IntPtr[]>();
        queue.Enqueue(root.Cast<GraphNodeStorage>());var edges=0;
        while(queue.Count>0) {
            var node=queue.Dequeue();
            if(node is null||node.Pointer==IntPtr.Zero)throw Invalid("GRAPH","dead graph node");
            if(!visited.Add(node.Pointer))continue;
            if(visited.Count>8192)throw Invalid("LIMIT","more than8192 graph nodes");
            var item=node.TryCast<GameItem>();
            if(item is not null) {
                NativeAccess.RequireItem(item);
                if(string.IsNullOrWhiteSpace(item.identifier)||item.uniqueId< -1||item.uniqueId==-1&&!allowUnassigned||item.uniqueId>=0&&!ids.Add(item.uniqueId))throw Invalid("IDENTITY","missing definition or duplicate/negative instance ID");
                if(result.Count>=4096)throw Invalid("LIMIT","more than4096 items");
                result.Add(item);
            }
            var children=node.children;if(children is null){adjacency[node.Pointer]=Array.Empty<IntPtr>();continue;}
            if(children.Count>8192-edges)throw Invalid("LIMIT","more than8192 graph edges");
            edges+=children.Count;
            var childPointers=new List<IntPtr>();foreach(var child in children){if(child is null||child.Pointer==IntPtr.Zero)throw Invalid("GRAPH","dead child node");childPointers.Add(child.Pointer);queue.Enqueue(child);}
            adjacency[node.Pointer]=childPointers.ToArray();
        }
        // 原生编码可能递归；必须在进入原生前拒绝环，不能只靠visited避免托管循环。
        var degrees=adjacency.Keys.ToDictionary(p=>p,_=>0);
        foreach(var children in adjacency.Values)foreach(var child in children)degrees[child]++;
        var ready=new Queue<IntPtr>(degrees.Where(p=>p.Value==0).Select(p=>p.Key));var processed=0;
        while(ready.Count>0){var id=ready.Dequeue();processed++;foreach(var child in adjacency[id])if(--degrees[child]==0)ready.Enqueue(child);}
        if(processed!=adjacency.Count)throw Invalid("RELATION","native graph contains a cycle");
        if(!NativeAccess.SameRun(run))throw Invalid("STALE","run changed during graph capture");
        return result.AsReadOnly();
    }

    /// <summary>输出游戏原生SaveState JSON，编码前后核对实例/数量与完整性；不调用任何保存文件函数。</summary>
    public static string Encode(GameItem root)
    {
        Enter();
        try {
            var run=NativeAccess.RequireRun();var before=Stamp(Items(root));
            using var scratch=new NativeScratch();
            var nodes=SaveManager.EncodeNodes(root.Cast<GraphNodeStorage>())??throw Invalid("ENCODE","native encoder returned null");
            var contract=ReadNodes(nodes);CheckExact(before,contract);
            if(!before.SequenceEqual(Stamp(Items(root)))||!NativeAccess.SameRun(run))throw Invalid("STALE","source changed during encode");
            var state=new SaveState {saveItems=nodes};var json=JsonUtility.ToJson(state);
            ValidateJson(json);
            return json;
        }finally{_busy=false;}
    }

    /// <summary>
    /// 恢复一个未放置的完整图；自动核对当前周目所有可观察实例ID，并合并调用者额外保留的离图ID。
    /// 自定义onLoaded同步运行，发生异常可能已有回调副作用；停止批次，不能盲目重试。
    /// </summary>
    public static GameItem Decode(string json,IReadOnlySet<int>? existingIds=null)
    {
        Enter();
        Il2CppSystem.Collections.Generic.List<SaveItemNode>? nodes=null;GameItem? root=null;var nativeStarted=false;
        var run=GameSession.Identity!;
        try {
            // 按已核对的当前构建加载序列恢复；未知构建或缺少任一步骤时，创建原生图前拒绝。
            if(!CanDecode)throw Invalid("UNSUPPORTED",DecodeUnavailableReason??"native load sequence unavailable");
            ValidateJson(json);
            var parsed=JsonUtility.FromJson(json,Il2CppType.Of<SaveState>())?.TryCast<SaveState>()??throw Invalid("JSON","native SaveState unavailable");
            nodes=parsed.saveItems??throw Invalid("JSON","saveItems missing");var contract=ReadNodes(nodes);
            if(existingIds?.Count>65536)throw Invalid("LIMIT","too many external instance IDs");
            var live=InventoryApi.CaptureRunItems(GameSession.Resolve(run)).Select(i=>i.uniqueId).ToHashSet();
            if(existingIds is not null)live.UnionWith(existingIds);
            if(contract.Any(n=>live.Contains(n.UniqueId)))throw Invalid("CONFLICT","instance ID already exists; detach/delete source explicitly before restoring");
            if(!NativeAccess.SameRun(run))throw Invalid("STALE","run changed before decode");
            using var scratch=new NativeScratch();
            nativeStarted=true;root=SaveManager.DecodeNodes(nodes);
            NativeAccess.RequireItem(root);
            if(root.parentInventory is not null)throw Invalid("DECODE","native root is already placed");
            var links=new List<GameItem>();var pointers=new HashSet<IntPtr>();
            foreach(var node in nodes) {
                var item=NativeAccess.RequireItem(node.tempLink);
                if(!pointers.Add(item.Pointer)||item.uniqueId!=node.uniqueId||item.identifier!=node.identifier||item.unitCount!=node.unitCount)
                    throw Invalid("DECODE","native tempLink identity/count mismatch");
                links.Add(item);
            }
            var maximum=contract.Max(n=>n.UniqueId);
            if(maximum==int.MaxValue)throw Invalid("IDENTITY","instance ID counter cannot advance");
            var store=GameSession.Resolve(run);store.currentUniqueId=Math.Max(store.currentUniqueId,maximum+1);
            // 调用本机构建已核对签名的加载辅助入口，只对新图执行一次；回调行为仍需游戏验证。
            foreach(var item in links) {
                if(!NativeAccess.SameRun(run))throw Invalid("STALE","onLoaded changed current run");
                SaveGraphLoader.Apply(item);
            }
            if(!NativeAccess.SameRun(run)||root.parentInventory is not null)throw Invalid("STALE","callback changed run or placed graph root");
            CheckExact(Stamp(Items(root)),contract);
            var restored=ReadNodes(SaveManager.EncodeNodes(root.Cast<GraphNodeStorage>()));
            if(!Relations(contract).SequenceEqual(Relations(restored)))throw Invalid("INCOMPLETE","restored parent/inventory relationships differ");
            return root;
        }catch(Exception e) {
            // Native decoder可能在返回root前失败。只销毁明确属于本次tempLink且仍未放置的根；
            // 回调若已把物品放进其它库存，则不越权删除，错误必须交给调用方核对。
            if(nativeStarted&&NativeAccess.SameRun(run)&&nodes is not null) {
                try {
                    var created=nodes.ToArray().Select(n=>n.tempLink).OfType<GameItem>().Where(i=>i.Pointer!=IntPtr.Zero).ToArray();
                    if(created.All(i=>i.parentInventory is null||created.Any(p=>p.Pointer==i.parentInventory.GetParentItem()?.Pointer)))
                        foreach(var item in created.Where(i=>i.parentInventory is null).ToArray())if(item.Pointer!=IntPtr.Zero)item.Destroy();
                }catch(Exception cleanup){throw new InvalidDataException("EZAPI_SAVEGRAPH_INDETERMINATE: decode and cleanup failed; inspect before retry. "+cleanup.Message,e);}
            }
            if(nativeStarted)throw new InvalidDataException("EZAPI_SAVEGRAPH_INDETERMINATE: native decode/callback failed; inspect before retry.",e);
            throw;
        }finally{_busy=false;}
    }

    private sealed record Node(int UniqueId,string ItemId,int Units,long Uuid,long[] Children,int[] Slots);
    private sealed record ItemStamp(int UniqueId,string ItemId,int Units);
    private static ItemStamp[] Stamp(IReadOnlyList<GameItem> items)=>items.Select(i=>new ItemStamp(i.uniqueId,i.identifier,i.unitCount)).OrderBy(i=>i.UniqueId).ToArray();
    private static void CheckExact(ItemStamp[] items,Node[] nodes)
    {
        var expected=nodes.Select(n=>new ItemStamp(n.UniqueId,n.ItemId,n.Units)).OrderBy(n=>n.UniqueId);
        if(!items.SequenceEqual(expected))throw Invalid("INCOMPLETE","encoded/restored node identities and counts differ from actual graph");
    }
    private static Node[] ReadNodes(Il2CppSystem.Collections.Generic.List<SaveItemNode> nodes)
    {
        if(nodes.Count is <1 or >4096)throw Invalid("LIMIT","1..4096 nodes required");
        var result=new List<Node>();var uuids=new HashSet<long>();var ids=new HashSet<int>();var edges=0;var shapeCells=0;
        void CheckShape(GridShapeBuilder? shape) {
            if(shape is null)return; // 缺省形状仍由原生工厂负责；显式形状须为有限且数据完整的矩阵。
            if(shape.width is <1 or >64||shape.height is <1 or >64||shape.outside!=0||shape.data is null||
                shape.data.Length!=shape.width*shape.height||Math.Abs((long)shape.minX)>1_000_000||Math.Abs((long)shape.minY)>1_000_000||shape.orientation is <0 or >3)
                throw Invalid("SHAPE","finite explicit shape <=64x64 with complete data required");
            shapeCells+=shape.data.Length;if(shapeCells>65536)throw Invalid("LIMIT","more than65536 saved shape cells");
        }
        foreach(var n in nodes) {
            if(n is null||n.Pointer==IntPtr.Zero||string.IsNullOrWhiteSpace(n.identifier)||n.identifier.Length>160||n.unitCount<=0||n.uniqueId<0||n.uniqueId==int.MaxValue||
                !ids.Add(n.uniqueId)||!uuids.Add(n.uuid)||!Il2Cpp.DirectoryMaster.Has<GameItem>(n.identifier))throw Invalid("NODE","invalid/duplicate ID, count or unavailable definition");
            CheckShape(n.itemShape);CheckShape(n.itemModifiedShape);
            var children=n.childItems?.ToArray()?.ToArray()??Array.Empty<long>();
            var slots=n.childItemInventoryNode;
            if(children.Length!=(slots is null?0:slots.Count)||children.Length>8192-edges)throw Invalid("RELATION","unpaired/oversized child relationship");
            edges+=children.Length;
            if(slots is not null&&slots.ToArray().Any(s=>s is <0 or >8191))throw Invalid("RELATION","child inventory index outside0..8191");
            result.Add(new(n.uniqueId,n.identifier,n.unitCount,n.uuid,children,slots?.ToArray()?.ToArray()??Array.Empty<int>()));
        }
        var parentCounts=result.ToDictionary(n=>n.Uuid,_=>0);
        foreach(var n in result)foreach(var child in n.Children) {
            if(!parentCounts.ContainsKey(child)||child==n.Uuid||++parentCounts[child]>1)throw Invalid("RELATION","missing child, self-reference or multiple parents");
        }
        var roots=parentCounts.Where(p=>p.Value==0).Select(p=>p.Key).ToArray();
        if(roots.Length!=1)throw Invalid("RELATION","exactly one graph root required");
        var byUuid=result.ToDictionary(n=>n.Uuid);var seen=new HashSet<long>();var queue=new Queue<long>();queue.Enqueue(roots[0]);
        while(queue.Count>0){var id=queue.Dequeue();if(!seen.Add(id))throw Invalid("RELATION","cyclic graph");foreach(var child in byUuid[id].Children)queue.Enqueue(child);}
        if(seen.Count!=result.Count)throw Invalid("RELATION","disconnected graph");
        return result.ToArray();
    }
    private static string[] Relations(Node[] nodes)
    {
        var ids=nodes.ToDictionary(n=>n.Uuid,n=>n.UniqueId);
        return nodes.SelectMany(n=>n.Children.Select((c,i)=>n.UniqueId+":"+ids[c]+":"+n.Slots[i])).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
    }
    // 先用有界托管解析器限制体积、深度与重复键，再交给Unity原生解析；不接受无限制外部JSON。
    private static void ValidateJson(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>MaxBytes||Encoding.UTF8.GetByteCount(json)>MaxBytes)throw Invalid("LIMIT","JSON must be nonempty and <=8MiB UTF-8");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});
        var count=0;
        void Walk(JsonElement e) {
            if(++count>262144)throw Invalid("LIMIT","too many JSON elements");
            if(e.ValueKind==JsonValueKind.Object) {
                var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!keys.Add(p.Name))throw Invalid("JSON","duplicate JSON property");Walk(p.Value);}
            } else if(e.ValueKind==JsonValueKind.Array)foreach(var value in e.EnumerateArray())Walk(value);
        }
        Walk(doc.RootElement);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object||!doc.RootElement.TryGetProperty("saveItems",out var items)||items.ValueKind!=JsonValueKind.Array||items.GetArrayLength() is <1 or >4096)
            throw Invalid("JSON","SaveState.saveItems array with1..4096 entries required");
    }
    private static void Enter(){NativeAccess.RequireRun();if(_busy)throw new InvalidOperationException("EZAPI_SAVEGRAPH_REENTRANT: graph operation already active.");_busy=true;}
    private static InvalidDataException Invalid(string code,string detail)=>new("EZAPI_SAVEGRAPH_"+code+": "+detail);


    /// <summary>SaveManager的堆是共享scratch，不属于存档业务状态；独立操作不污染当前保存过程。</summary>
    private sealed class NativeScratch:IDisposable
    {
        private readonly long _uuid=SaveManager.currentUUID;
        private readonly Il2CppSystem.Collections.Generic.Dictionary<GameItem,long>? _heap=SaveManager.itemHeap;
        private readonly Il2CppSystem.Collections.Generic.Dictionary<long,GameItem>? _dict=SaveManager.itemDict;
        private readonly string? _json=SaveManager.saveJson;
        internal NativeScratch(){SaveManager.itemHeap=new();SaveManager.itemDict=new();SaveManager.currentUUID=0;}
        public void Dispose(){SaveManager.itemHeap=_heap;SaveManager.itemDict=_dict;SaveManager.currentUUID=_uuid;SaveManager.saveJson=_json;}
    }
}
