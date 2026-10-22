using System.Collections.ObjectModel;
using Il2Cpp;
namespace EZapi;

public enum InventorySnapshotStatus { Ready, Unavailable, Invalid }
public enum TransferPreviewStatus { Ready, Unavailable, Invalid, SourceMismatch, Locked, NoWholeSlot }
/// <summary>纯托管原始形状；物品非零格占用，原生库存边界遮罩非零格禁止放置。Cells按全局包围框逐行编码。</summary>
public sealed record InventoryShape(int X, int Y, int Width, int Height, int Orientation, bool Flipped, string Cells)
{
    /// <summary>物品必须为0；库存边界必须为1。保留原生哨兵，参与快照相等比较。</summary>
    public byte OutsideBounds { get; init; }
}
/// <summary>NativeIdentity仅限本进程/本Epoch比较，不是存档身份，不可转回原生指针。</summary>
public sealed record NativeInventoryItem(long NativeIdentity, int InstanceId, string ItemId, int Units, bool PlayerOwned, InventoryShape? Shape);
public sealed record NativeInventorySnapshot(InventorySnapshotStatus Status, GameRunIdentity Run, long NativeIdentity,
    long ParentIdentity, bool InsertLocked, bool RemoveLocked, InventoryShape? Grid,
    IReadOnlyList<NativeInventoryItem> Items, string? Reason);
public sealed record NativeTransferPreview(TransferPreviewStatus Status, int ItemInstanceId, int Units, string? Reason)
{
    public bool CanTransfer => Status == TransferPreviewStatus.Ready;
}

/// <summary>主线程、当前已载入周目中的库存观察。快照可供显示；原生对象列表只允许同步使用。</summary>
public static class InventoryApi
{
    public static bool IsItemDragActive
    {
        get {
            NativeAccess.RequireRun();
            var drag = ItemMouseDragHandler.current;
            var group = ItemMultiSelectHandler.current;
            return drag is not null && drag.Pointer != IntPtr.Zero && drag.IsDraggingItem ||
                group is not null && group.Pointer != IntPtr.Zero && group.state is
                    ItemMultiSelectHandler.State.PendingGroupDrag or ItemMultiSelectHandler.State.GroupDragging;
        }
    }

    /// <summary>合并原生FindAllItem、saveBags和主库存的子图，指针去重；最多8192节点/4096物品，超限抛错，不返回截断结果。</summary>
    public static IReadOnlyList<GameItem> CaptureRunItems(PlayerStore store)
    {
        var run = NativeAccess.RequireRun();
        if (store is null || store.Pointer == IntPtr.Zero || GameSession.Resolve(run).Pointer != store.Pointer)
            throw new ArgumentException("EZAPI_INVENTORY_STORE: current PlayerStore required.");
        return CaptureScoped(store,()=>NativeAccess.SameRun(run));
    }
    // 生命周期BeforeLoad不能伪造已加载周目；以同步作用域检查复用同一有界遍历。
    internal static IReadOnlyList<GameItem> CaptureScoped(PlayerStore store,Func<bool> current)
    {
        GameSession.RequireThread();
        if(!current()||store.Pointer==IntPtr.Zero)throw new InvalidOperationException("EZAPI_INVENTORY_STALE: inventory scope expired.");
        var roots = store.FindAllItem(true) ?? throw new InvalidOperationException("EZAPI_INVENTORY_UNAVAILABLE: FindAllItem unavailable.");
        if (roots.Count > 4096) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: too many roots.");
        var queue = new Queue<GraphNodeStorage>();
        var visited = new HashSet<IntPtr>();
        void Add(GraphNodeStorage? node) {
            if (node is null || node.Pointer == IntPtr.Zero) throw new InvalidOperationException("EZAPI_INVENTORY_GRAPH: dead graph node.");
            if (!visited.Add(node.Pointer)) return;
            if (visited.Count > 8192) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: more than 8192 graph nodes.");
            queue.Enqueue(node);
        }
        foreach (var item in roots) Add(item?.TryCast<GraphNodeStorage>());
        if (store.gridInv is not null) Add(store.gridInv.TryCast<GraphNodeStorage>());
        var bags = store.saveBags;
        if (bags is not null) {
            if (bags.Count > 4096) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: too many save bags.");
            foreach (var entry in bags) Add(entry.Value?.TryCast<GraphNodeStorage>());
        }
        var result = new List<GameItem>();
        var ids = new HashSet<int>();
        var edges = 0;
        while (queue.Count > 0) {
            if(!current())throw new InvalidOperationException("EZAPI_INVENTORY_STALE: inventory scope expired during capture.");
            var node = queue.Dequeue();
            var item = node.TryCast<GameItem>();
            if (item is not null) {
                if(item.Pointer==IntPtr.Zero||item.unitCount<=0)throw new ArgumentException("EZAPI_NATIVE_ITEM: current live item required.");
                if (result.Count >= 4096) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: more than 4096 items.");
                if (!ids.Add(item.uniqueId)) throw new InvalidOperationException("EZAPI_INVENTORY_IDENTITY: duplicate live instance ID.");
                result.Add(item);
            }
            var children = node.children;
            if (children is null) continue;
            if (children.Count > 8192 - edges) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: too many graph edges.");
            edges += children.Count;
            foreach (var child in children) Add(child);
        }
        if (!current()) throw new InvalidOperationException("EZAPI_INVENTORY_STALE: inventory scope expired during capture.");
        return result.AsReadOnly();
    }

    public static bool IsPlayerOwned(GameItem item) => GeneralHelper.IsItemOwned(NativeAccess.RequireItem(item));

    /// <summary>只统计玩家拥有的实例；requested IDs缺席时返回0；不传过滤器则只列存在的ID。整数溢出抛错。</summary>
    public static IReadOnlyDictionary<string, int> CaptureOwnedItemCounts(PlayerStore store, IReadOnlyCollection<string>? relevantIds = null)
    {
        if (relevantIds?.Count > 4096) throw new ArgumentException("EZAPI_INVENTORY_LIMIT: too many requested IDs.");
        var filter = relevantIds is null ? null : new HashSet<string>(relevantIds, StringComparer.Ordinal);
        if (filter is not null && filter.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 160))
            throw new ArgumentException("EZAPI_INVENTORY_ID: nonempty native IDs required.");
        var run = NativeAccess.RequireRun();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        if (filter is not null) foreach (var id in filter) totals.Add(id, 0);
        foreach (var item in CaptureRunItems(store)) {
            if (!GeneralHelper.IsItemOwned(item) || filter is not null && !filter.Contains(item.identifier)) continue;
            if (string.IsNullOrEmpty(item.identifier) || item.unitCount <= 0)
                throw new InvalidOperationException("EZAPI_INVENTORY_IDENTITY: item changed during count.");
            totals[item.identifier] = checked(totals.GetValueOrDefault(item.identifier) + item.unitCount);
        }
        if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("EZAPI_INVENTORY_STALE: run changed during count.");
        return new ReadOnlyDictionary<string, int>(totals);
    }

    /// <summary>只读直接子项。失败返回空Items，绝不把部分读取标Ready；状态未就绪/线程错误沿用公共运行时异常。</summary>
    public static NativeInventorySnapshot CaptureDirect(GameInventory inventory, bool includeGeometry = false)
    {
        var run = NativeAccess.RequireRun();
        try { return InventoryChecks.Read(inventory, run, includeGeometry); }
        catch (Exception e) { return new(InventorySnapshotStatus.Invalid, run, inventory?.Pointer.ToInt64() ?? 0,
            0, true, true, null, Array.Empty<NativeInventoryItem>(), e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>整件/整堆落入独立空位的预检，不允许目标堆叠；不移动物品，不保证稍后的提交仍有效。</summary>
    public static NativeTransferPreview PreviewWholeGridTransfer(GameItem item, GameGridInventory source, GameGridInventory destination)
    {
        NativeAccess.RequireItem(item);
        var id = item.uniqueId; var units = item.unitCount;
        try {
            if (source is null || destination is null || source.Pointer == IntPtr.Zero || destination.Pointer == IntPtr.Zero)
                return new(TransferPreviewStatus.Invalid, id, units, "EZAPI_INVENTORY_TARGET");
            if (source.Pointer == destination.Pointer || item.parentInventory?.Pointer != source.Pointer)
                return new(TransferPreviewStatus.SourceMismatch, id, units, "EZAPI_TRANSFER_SOURCE");
            var run = GameSession.Identity!;
            var beforeSource = InventoryChecks.Read(source, run); var beforeTarget = InventoryChecks.Read(destination, run);
            if (!InventoryChecks.CanMove(item, source, destination) || item.MaxNumRemove() < units)
                return new(TransferPreviewStatus.Locked, id, units, "EZAPI_TRANSFER_LOCKED: ownership, drag, locks or nesting rejected.");
            var marker = destination.TryFindOneValidInventorySlot(item, false);
            if (!InventoryChecks.Marker(marker, item, destination, units, true))
                return new(TransferPreviewStatus.NoWholeSlot, id, units, "EZAPI_TRANSFER_NO_WHOLE_SLOT");
            if (!InventoryChecks.Equal(beforeSource, InventoryChecks.Read(source, run)) ||
                !InventoryChecks.Equal(beforeTarget, InventoryChecks.Read(destination, run)))
                return new(TransferPreviewStatus.Invalid, id, units, "EZAPI_INVENTORY_STALE");
            return new(TransferPreviewStatus.Ready, id, units, null);
        } catch (Exception e) { return new(TransferPreviewStatus.Unavailable, id, units, e.GetType().Name + ": " + e.Message); }
    }
}

internal static class InventoryChecks
{
    internal static InventoryShape Shape(GridShape shape, bool inventoryBoundary = false)
    {
        if (shape is null || shape.Pointer == IntPtr.Zero || shape.globalWidth < 1 || shape.globalHeight < 1 ||
            shape.globalWidth > 64 || shape.globalHeight > 64 || shape.GetOutsideBounds() != (inventoryBoundary ? 1 : 0) ||
            Math.Abs((long)shape.minX) > 1_000_000 || Math.Abs((long)shape.minY) > 1_000_000)
            throw new InvalidOperationException("EZAPI_INVENTORY_SHAPE: bounded finite shape required (1..64 per axis); " +
                (shape is null || shape.Pointer == IntPtr.Zero ? "dead shape" :
                $"x={shape.minX}, y={shape.minY}, width={shape.globalWidth}, height={shape.globalHeight}, outside={shape.GetOutsideBounds()}."));
        var bytes = new byte[shape.globalWidth * shape.globalHeight];
        for (var y = 0; y < shape.globalHeight; y++) for (var x = 0; x < shape.globalWidth; x++)
            bytes[y * shape.globalWidth + x] = shape.Get(shape.minX + x, shape.minY + y);
        return new(shape.minX, shape.minY, shape.globalWidth, shape.globalHeight, shape.orientation, shape.flipped, Convert.ToHexString(bytes))
            { OutsideBounds = shape.GetOutsideBounds() };
    }
    internal static NativeInventoryItem Item(GameItem item, bool includeGeometry = true)
    {
        NativeAccess.RequireItem(item);
        if (string.IsNullOrEmpty(item.identifier)) throw new InvalidOperationException("EZAPI_INVENTORY_IDENTITY: missing item ID.");
        return new(item.Pointer.ToInt64(), item.uniqueId, item.identifier, item.unitCount,
            GeneralHelper.IsItemOwned(item), !includeGeometry || item.modifiedShape is null ? null : Shape(item.modifiedShape));
    }
    internal static NativeInventorySnapshot Read(GameInventory inventory, GameRunIdentity run, bool includeGeometry = true)
    {
        if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("EZAPI_INVENTORY_STALE: run expired.");
        if (inventory is null || inventory.Pointer == IntPtr.Zero) throw new ArgumentException("EZAPI_INVENTORY_TARGET: live inventory required.");
        var list = inventory.childItems ?? throw new InvalidOperationException("EZAPI_INVENTORY_UNAVAILABLE: no child list.");
        if (list.Count > 4096) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: more than 4096 direct items.");
        var items = new List<NativeInventoryItem>(list.Count);
        var ids = new HashSet<int>(); var pointers = new HashSet<IntPtr>(); var cells = 0;
        foreach (var item in list) {
            if (item is null || item.Pointer == IntPtr.Zero || item.parentInventory?.Pointer != inventory.Pointer ||
                !ids.Add(item.uniqueId) || !pointers.Add(item.Pointer))
                throw new InvalidOperationException("EZAPI_INVENTORY_IDENTITY: duplicate ID/pointer or incorrect parent.");
            var record = Item(item, includeGeometry);
            cells += record.Shape is null ? 0 : record.Shape.Width * record.Shape.Height;
            if (cells > 65536) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT: too many shape cells.");
            items.Add(record);
        }
        var grid = includeGeometry ? inventory.TryCast<GameGridInventory>() : null;
        var snapshot = new NativeInventorySnapshot(InventorySnapshotStatus.Ready, run, inventory.Pointer.ToInt64(),
            inventory.parent?.Pointer.ToInt64() ?? 0, inventory.IsInsertLocked(), inventory.IsRemoveLocked(),
            grid is null ? null : Shape(grid.inventoryShape, inventoryBoundary:true), items.OrderBy(i => i.NativeIdentity).ToList().AsReadOnly(), null);
        if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("EZAPI_INVENTORY_STALE");
        return snapshot;
    }
    internal static bool Equal(NativeInventorySnapshot a, NativeInventorySnapshot b) =>
        a.Status == InventorySnapshotStatus.Ready && b.Status == InventorySnapshotStatus.Ready && a.Run == b.Run &&
        a.NativeIdentity == b.NativeIdentity && a.ParentIdentity == b.ParentIdentity && a.Grid == b.Grid &&
        a.InsertLocked == b.InsertLocked && a.RemoveLocked == b.RemoveLocked && a.Items.SequenceEqual(b.Items);
    internal static bool CanMove(GameItem item, GameInventory source, GameGridInventory target) =>
        source.Pointer != IntPtr.Zero && source.Pointer != target.Pointer && item.parentInventory?.Pointer == source.Pointer &&
        !InventoryApi.IsItemDragActive && GeneralHelper.IsItemOwned(item) && item.MayRemove() &&
        !source.IsRemoveLocked() && !target.IsInsertLocked() && GeneralHelper.MayPlayerInsertInto(target) &&
        GeneralHelper.MayOwnershipAcceptItem(item, target) && !Contains(item, target);

    // 有界向下扫描，不依赖可能递归的全局原生FindChild，不允许把物品放入自己的子库存。
    internal static bool Contains(GameItem item, GameInventory target)
    {
        var root = item.TryCast<GraphNodeStorage>() ?? throw new InvalidOperationException("EZAPI_INVENTORY_GRAPH");
        var queue = new Queue<GraphNodeStorage>(); var visited = new HashSet<IntPtr>();
        queue.Enqueue(root); var edges = 0;
        while (queue.Count > 0) {
            var node = queue.Dequeue();
            if (node is null || node.Pointer == IntPtr.Zero) throw new InvalidOperationException("EZAPI_INVENTORY_GRAPH");
            if (node.Pointer == target.Pointer) return true;
            if (!visited.Add(node.Pointer)) continue;
            if (visited.Count > 8192) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT");
            var children = node.children; if (children is null) continue;
            if (children.Count > 8192 - edges) throw new InvalidOperationException("EZAPI_INVENTORY_LIMIT");
            edges += children.Count;
            foreach (var child in children) queue.Enqueue(child);
        }
        return false;
    }
    internal static bool Marker(SlotMarker? marker, GameItem item, GameGridInventory target, int amount, bool emptyOnly = false) =>
        marker is not null && marker.Pointer != IntPtr.Zero && marker.IsValid() && marker.item?.Pointer == item.Pointer &&
        marker.inventory?.Pointer == target.Pointer && marker.numTransfer >= amount &&
        (!emptyOnly || marker.targetItem is null && marker.itemGridShape is not null);
}
