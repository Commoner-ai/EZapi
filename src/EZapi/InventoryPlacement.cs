using System.Diagnostics;
using Il2Cpp;
namespace EZapi;

/// <summary>仅当前同步调用有效的原生落点。Shape是独立克隆，Item仍由调用者负责交付或销毁；计划不预留空间。</summary>
public sealed record InventoryPlacement(GameItem Item, GridShape Shape);

/// <summary>固定目标现有物品，仅在内存中规划未放置单件。不做全局排序，不提交接纳。</summary>
public static class InventoryPlacementApi
{
    public static bool IsAvailable => GameSession.TryResolve(null, out _);

    /// <summary>
    /// 最多64产品、网格64×64、8192候选、65536格检查、40ms；任一预算耗尽整组失败。
    /// 按占格从大到小确定性贪心规划，不保证找到所有数学上可行的布局；不返回部分成功。
    /// </summary>
    public static IReadOnlyList<InventoryPlacement> PlanWholeGrid(GameGridInventory inventory, IReadOnlyList<GameItem> products)
    {
        var run = NativeAccess.RequireRun();
        if (products is null || products.Count > 64) throw new ArgumentException("EZAPI_PLACEMENT_INPUT: 0..64 detached single products required.");
        var before = InventoryChecks.Read(inventory, run);
        if (InventoryApi.IsItemDragActive || before.InsertLocked || !GeneralHelper.MayPlayerInsertInto(inventory))
            throw new InvalidOperationException("EZAPI_PLACEMENT_LOCKED: native insertion is locked or dragging.");
        if (before.Grid is null) throw new ArgumentException("EZAPI_PLACEMENT_INPUT: finite grid required.");
        var originals = new NativeInventoryItem[products.Count];
        var pointers = new HashSet<IntPtr>();
        var productCells = 0;
        for (var i = 0; i < products.Count; i++) {
            var item = NativeAccess.RequireItem(products[i]);
            if (item.unitCount != 1 || item.parentInventory is not null || !pointers.Add(item.Pointer) || item.shape is null)
                throw new ArgumentException("EZAPI_PLACEMENT_INPUT: unique detached single products required.");
            if (!GeneralHelper.MayOwnershipAcceptItem(item, inventory) || InventoryChecks.Contains(item, inventory))
                throw new InvalidOperationException("EZAPI_PLACEMENT_REJECTED: ownership or nesting rejected.");
            originals[i] = InventoryChecks.Item(item);
            productCells += originals[i].Shape is {} s ? s.Width * s.Height : 0;
            if (productCells > 65536) throw new ArgumentException("EZAPI_PLACEMENT_INPUT: product shapes exceed 65536 cells.");
        }
        var watch = Stopwatch.StartNew(); var candidates = 0; var cells = 0;
        void Budget(int cost = 0) {
            cells += cost;
            if (candidates > 8192 || cells > 65536 || watch.ElapsedMilliseconds > 40)
                throw new InvalidOperationException("EZAPI_PLACEMENT_BUDGET: bounded search stopped; no item was moved.");
        }
        var area = before.Grid;
        // 游戏inventoryShape是边界遮罩：内部0允许，非零禁止；不能按物品占格的含义解释。
        var blocked = Convert.FromHexString(area.Cells);
        var occupied = new bool[blocked.Length];
        bool Fits(InventoryShape shape, bool mark) {
            var data = Convert.FromHexString(shape.Cells); Budget(data.Length);
            for (var y = 0; y < shape.Height; y++) for (var x = 0; x < shape.Width; x++) {
                if (data[y * shape.Width + x] == 0) continue;
                var gx = shape.X + x - area.X; var gy = shape.Y + y - area.Y;
                if (gx < 0 || gy < 0 || gx >= area.Width || gy >= area.Height) return false;
                var cell = gy * area.Width + gx;
                if (blocked[cell] != 0 || occupied[cell]) return false;
            }
            if (mark) for (var y = 0; y < shape.Height; y++) for (var x = 0; x < shape.Width; x++)
                if (data[y * shape.Width + x] != 0) occupied[(shape.Y + y - area.Y) * area.Width + shape.X + x - area.X] = true;
            return true;
        }
        foreach (var existing in before.Items) {
            if (existing.Shape is null || !Fits(existing.Shape, true))
                throw new InvalidOperationException("EZAPI_PLACEMENT_GRID: existing item overlaps or lies outside the grid.");
        }
        var result = new InventoryPlacement[products.Count];
        var order = Enumerable.Range(0, products.Count).OrderByDescending(i => originals[i].Shape is {} s ? s.Width * s.Height : 0);
        foreach (var index in order) {
            var item = products[index]; GridShape? placed = null;
            // 保留原生翻转状态；旋转只通过公开Builder，最终仍由原生TryInventorySlot检查。
            for (var rotation = 0; rotation < 4 && placed is null; rotation++) {
                var template = new GridShapeBuilder(item.shape).SetTransform(0, 0, item.shape.flipped,
                    (item.shape.orientation + rotation) % 4).Clone();
                var dimensions = InventoryChecks.Shape(template);
                for (var y = area.Y; y <= area.Y + area.Height - dimensions.Height && placed is null; y++)
                for (var x = area.X; x <= area.X + area.Width - dimensions.Width; x++) {
                    candidates++; Budget();
                    var candidateShape = dimensions with { X = x, Y = y };
                    if (!Fits(candidateShape, false)) continue;
                    var nativeShape = new GridShapeBuilder(template).SetPosition(x, y).Clone();
                    var marker = ((GameInventory)inventory).TryInventorySlot(item, 1, nativeShape, null);
                    if (!InventoryChecks.Marker(marker, item, inventory, 1, true)) continue;
                    var actual = InventoryChecks.Shape(marker!.itemGridShape);
                    // 禁止原生自动挪位/合堆冒充当前候选；保留其它Mod的准入否决。
                    if (actual != candidateShape || !Fits(actual, true)) continue;
                    placed = marker.itemGridShape.Clone(); break;
                }
            }
            if (placed is null) throw new InvalidOperationException("EZAPI_PLACEMENT_NO_PLAN: greedy search found no admissible layout; no item was moved.");
            result[index] = new(item, placed);
        }
        Budget();
        if (!InventoryChecks.Equal(before, InventoryChecks.Read(inventory, run)) || InventoryApi.IsItemDragActive)
            throw new InvalidOperationException("EZAPI_INVENTORY_STALE: target changed during planning.");
        for (var i = 0; i < products.Count; i++)
            if (products[i].parentInventory is not null || InventoryChecks.Item(products[i]) != originals[i])
                throw new InvalidOperationException("EZAPI_INVENTORY_STALE: product changed during planning.");
        return Array.AsReadOnly(result);
    }
}
