using Il2Cpp;
namespace EZapi;

/// <summary>Moved为经源/目标差额确认的数量；Indeterminate=true时停止此批次并人工核对，不能盲目重试。</summary>
public sealed record ItemTransferResult(int Moved, bool Indeterminate, string Status, int NativeReported = 0, string? Error = null);

/// <summary>单次原生接纳，支持拆分/合堆/整件；不改父图、不直接改数量、不自动保存或重试。</summary>
public static class ItemTransferApi
{
    private static bool _moving;
    public static ItemTransferResult Move(GameItem item, GameGridInventory destination, int maximum)
    {
        var run = NativeAccess.RequireRun(); NativeAccess.RequireItem(item);
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum), "EZAPI_TRANSFER_AMOUNT: positive maximum required.");
        if (_moving) return new(0, false, "Reentrant", Error: "EZAPI_TRANSFER_REENTRANT");
        _moving = true;
        var attempted = false;
        var nativeReported = 0;
        NativeInventorySnapshot? sourceBefore = null, targetBefore = null;
        GameInventory? source = null;
        NativeInventoryItem? original = null;
        var request = 0;
        try {
            source = item.parentInventory;
            if (source is null || destination is null || destination.Pointer == IntPtr.Zero || !InventoryChecks.CanMove(item, source, destination))
                return new(0, false, "Rejected", Error: "EZAPI_TRANSFER_REJECTED: source, ownership, locks, dragging or nesting rejected.");
            sourceBefore = InventoryChecks.Read(source, run); targetBefore = InventoryChecks.Read(destination, run);
            // 同一原生实例不得同时出现在两个直接库存。
            if (sourceBefore.Items.Select(i => i.NativeIdentity).Intersect(targetBefore.Items.Select(i => i.NativeIdentity)).Any())
                return new(0, false, "Rejected", Error: "EZAPI_INVENTORY_IDENTITY: overlapping inventories.");
            if (sourceBefore.Items.Select(i => i.InstanceId).Intersect(targetBefore.Items.Select(i => i.InstanceId)).Any())
                return new(0, false, "Rejected", Error: "EZAPI_INVENTORY_IDENTITY: duplicate instance IDs across inventories.");
            original = sourceBefore.Items.SingleOrDefault(i => i.NativeIdentity == item.Pointer.ToInt64());
            if (original is null) return new(0, false, "Rejected", Error: "EZAPI_TRANSFER_SOURCE");
            request = Math.Min(maximum, Math.Min(original.Units, item.MaxNumRemove()));
            if (request <= 0) return new(0, false, "Rejected", Error: "EZAPI_TRANSFER_LOCKED");
            var marker = destination.TryFindOneValidInventorySlot(item, false);
            if (marker is null || marker.numTransfer <= 0) return new(0, false, "NoSlot");
            request = Math.Min(request, marker.numTransfer);
            if (!InventoryChecks.Marker(marker, item, destination, request)) return new(0, false, "NoSlot");
            // 原生准入委托可能改变库存；写前重读，不能复用已失效的快照。
            if (!InventoryChecks.CanMove(item, source, destination) || item.MaxNumRemove() < request ||
                !InventoryChecks.Equal(sourceBefore, InventoryChecks.Read(source, run)) ||
                !InventoryChecks.Equal(targetBefore, InventoryChecks.Read(destination, run)) || !marker.IsValid())
                return new(0, false, "Stale", Error: "EZAPI_INVENTORY_STALE");
            attempted = true;
            nativeReported = marker.TryAcceptOnce(request); // 恰好一次；即使抛异常也不能重试或先Expel。
            return Verify(null);
        } catch (Exception e) {
            var error = e.GetType().Name + ": " + e.Message;
            return attempted ? Verify(error) : new(0, false, "Rejected", Error: error);
        } finally { _moving = false; }

        ItemTransferResult Verify(string? error)
        {
            try {
                if (!NativeAccess.SameRun(run)) return new(0, true, "Indeterminate", nativeReported, "EZAPI_INVENTORY_STALE: run changed after write.");
                var sourceAfter = InventoryChecks.Read(source!, run); var targetAfter = InventoryChecks.Read(destination, run);
                var check = InventoryTransferAudit.Check(sourceBefore!, targetBefore!, sourceAfter, targetAfter, original!, request);
                if (!check.Valid || error is not null || nativeReported != check.Moved)
                    return new(check.Valid ? check.Moved : 0, true, "Indeterminate", nativeReported,
                        error ?? check.Error ?? "EZAPI_TRANSFER_VERIFY: native return differs from observed transfer.");
                return new(check.Moved, false, check.Moved == 0 ? "Rejected" : check.Moved < Math.Min(maximum, original!.Units) ? "Partial" : "Moved", nativeReported);
            } catch (Exception e) { return new(0, true, "Indeterminate", nativeReported, "EZAPI_TRANSFER_VERIFY: " + e.GetType().Name + ": " + e.Message); }
        }
    }
}

/// <summary>纯托管写后审计；可独立测试，不把原生返回值作为事实，不触碰可能已被合堆销毁的原物品。</summary>
internal static class InventoryTransferAudit
{
    internal sealed record Result(bool Valid, int Moved, string? Error = null);
    internal static Result Check(NativeInventorySnapshot sb, NativeInventorySnapshot tb, NativeInventorySnapshot sa,
        NativeInventorySnapshot ta, NativeInventoryItem original, int maximum)
    {
        bool SameInventory(NativeInventorySnapshot a, NativeInventorySnapshot b) => a.Status == InventorySnapshotStatus.Ready &&
            b.Status == InventorySnapshotStatus.Ready && a.Run == b.Run && a.NativeIdentity == b.NativeIdentity &&
            a.ParentIdentity == b.ParentIdentity && a.Grid == b.Grid && a.InsertLocked == b.InsertLocked && a.RemoveLocked == b.RemoveLocked;
        Result Fail() => new(false, 0, "EZAPI_TRANSFER_VERIFY: identity, quantity, ownership or unrelated inventory state changed.");
        if (maximum < 1 || !SameInventory(sb, sa) || !SameInventory(tb, ta) || sb.Run != tb.Run || sb.NativeIdentity == tb.NativeIdentity) return Fail();
        if (sa.Items.Select(i => i.NativeIdentity).Intersect(ta.Items.Select(i => i.NativeIdentity)).Any()) return Fail();
        var allAfter = sa.Items.Concat(ta.Items).ToArray();
        if (allAfter.Select(i => i.InstanceId).Distinct().Count() != allAfter.Length) return Fail();
        var sourceOriginal = sb.Items.SingleOrDefault(i => i.NativeIdentity == original.NativeIdentity);
        if (sourceOriginal != original || !original.PlayerOwned) return Fail();
        var remaining = sa.Items.SingleOrDefault(i => i.NativeIdentity == original.NativeIdentity);
        var unitsLeft = remaining?.Units ?? 0;
        var moved = original.Units - unitsLeft;
        if (moved < 0 || moved > maximum || remaining is not null && remaining != original with { Units = unitsLeft }) return Fail();
        if (!sb.Items.Where(i => i.NativeIdentity != original.NativeIdentity).SequenceEqual(sa.Items.Where(i => i.NativeIdentity != original.NativeIdentity))) return Fail();
        long added = 0; var changedTargetItems = 0;
        var oldTarget = tb.Items.ToDictionary(i => i.NativeIdentity);
        foreach (var after in ta.Items) {
            if (!oldTarget.Remove(after.NativeIdentity, out var before)) {
                if (after.ItemId != original.ItemId || !after.PlayerOwned || after.Units < 1) return Fail();
                // 新生成的拆分实例不能冒用其它源物品的原生身份。
                if (sb.Items.Any(i => i.NativeIdentity == after.NativeIdentity && i.NativeIdentity != original.NativeIdentity)) return Fail();
                if (after.NativeIdentity == original.NativeIdentity && (after.InstanceId != original.InstanceId || unitsLeft != 0)) return Fail();
                added += after.Units;
                changedTargetItems++;
            } else {
                var delta = (long)after.Units - before.Units;
                if (delta < 0 || after != before with { Units = after.Units } ||
                    delta > 0 && (after.ItemId != original.ItemId || !after.PlayerOwned)) return Fail();
                added += delta;
                if (delta > 0) changedTargetItems++;
            }
        }
        if (oldTarget.Count != 0 || added != moved || moved > 0 && changedTargetItems != 1) return Fail();
        // 0移动必须完全相同，包括坐标/方向，不能把重排当成拒绝。
        if (moved == 0 && !tb.Items.SequenceEqual(ta.Items)) return Fail();
        return new(true, moved);
    }
}
