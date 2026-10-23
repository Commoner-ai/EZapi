using Il2Cpp;
namespace EZapi;

/// <summary>Only Present/StagedInMemory with a Stamp permit subsequent preflight; neither means the game has saved or delivered anything.</summary>
public sealed record DeliveryReceiptStampResult(ItemDataStatus Status, DeliveryReceiptStamp? Stamp = null, string? Reason = null);

/// <summary>Explicit main-thread receipt tagging/scans over official item tags. Callers retain ledger, payment, native handoff and save policy.</summary>
public static class ItemReceiptApi
{
    /// <summary>Aggregate JSON character budget per full scan pass; excess returns no partial receipt evidence.</summary>
    public const int MaximumJsonCharacters = 1024 * 1024;
    private static bool _working;

    /// <summary>
    /// Stamp one caller-owned detached factory item using ItemDataApi. Refuses occupied/different receipt tags; matching existing data is a no-op.
    /// Use a key dedicated to delivery receipts. Never call with a wrapper already handed to native delivery, including after native failure.
    /// </summary>
    public static DeliveryReceiptStampResult StampDetached(GameItem item, string ownerId, string key, DeliveryReceipt receipt)
    {
        if (!GameSession.OnThread) return Fail(ItemDataStatus.Disabled, "THREAD");
        if (!DeliveryReceiptAudit.ValidAddress(ownerId, key) || !DeliveryReceiptAudit.ValidReceipt(receipt)) return Fail(ItemDataStatus.Invalid, "INPUT");
        if (_working) return Fail(ItemDataStatus.Conflict, "REENTRY");
        _working = true;
        var staged = false;
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown || !InventoryFreezeApi.IsAvailable) return Fail(ItemDataStatus.Unavailable, "BUILD");
            var original = InventoryChecks.Item(item, false);
            if (!original.PlayerOwned || item.parentInventory is not null || original.ItemId != receipt.ItemId || original.Units != receipt.Units)
                return Fail(ItemDataStatus.Conflict, "DETACHED");
            if (InventoryFreezeApi.IsFrozen(item)) return Fail(ItemDataStatus.Frozen, "FROZEN");
            if (InventoryApi.IsItemDragActive) return Fail(ItemDataStatus.Conflict, "BUSY");
            var read = ItemDataApi.Read<DeliveryReceipt>(item, ownerId, key);
            if (read.Status == ItemDataStatus.Present && read.Value != receipt || read.Status == ItemDataStatus.VersionMismatch)
                return Fail(ItemDataStatus.Conflict, "TAG");
            if (read.Status is not (ItemDataStatus.Present or ItemDataStatus.Missing)) return new(read.Status, Reason: read.Reason);
            if (!NativeAccess.SameRun(run) || InventoryChecks.Item(item, false) != original || item.parentInventory is not null)
                return Fail(ItemDataStatus.Conflict, "STALE");
            var status = ItemDataStatus.Present;
            if (read.Status == ItemDataStatus.Missing)
            {
                var write = ItemDataApi.Stage(item, read.Revision!, receipt);
                if (write.Status != ItemDataStatus.StagedInMemory) return new(write.Status, Reason: write.Reason);
                staged = true; status = write.Status;
            }
            var check = ItemDataApi.Read<DeliveryReceipt>(item, ownerId, key);
            if (check.Status != ItemDataStatus.Present || check.Value != receipt || !NativeAccess.SameRun(run) ||
                InventoryChecks.Item(item, false) != original || item.parentInventory is not null ||
                !InventoryFreezeApi.IsAvailable || InventoryFreezeApi.IsFrozen(item) || InventoryApi.IsItemDragActive || !NativeAccess.SameRun(run) ||
                InventoryChecks.Item(item, false) != original || item.parentInventory is not null)
                return Fail(staged ? ItemDataStatus.Indeterminate : ItemDataStatus.Conflict, "STALE");
            return new(status, new(run, ownerId, key, original, receipt));
        }
        catch (Exception error) { return Fail(staged ? ItemDataStatus.Indeterminate : ItemDataStatus.Unavailable, "STAMP", error.GetType().Name); }
        finally { _working = false; }
    }

    /// <summary>
    /// Capture the current run twice with fresh native discovery and compare all observed fields. Receipts use ItemDataApi version1.
    /// Bounded to4096 items/1MiB receipt JSON per pass. No cached wrappers or partial success; do not poll every UI frame.
    /// </summary>
    public static DeliveryReceiptSnapshot Capture(string ownerId, string key)
    {
        DeliveryReceiptSnapshot FailScan(DeliveryReceiptCaptureStatus status, string reason) => new(status, null, ownerId, key,
            Array.Empty<DeliveryReceiptItem>(), reason);
        if (!GameSession.OnThread) return FailScan(DeliveryReceiptCaptureStatus.Unavailable, "EZAPI_RECEIPT_THREAD: initialized game main thread required.");
        if (!DeliveryReceiptAudit.ValidAddress(ownerId, key)) return FailScan(DeliveryReceiptCaptureStatus.Invalid, "EZAPI_RECEIPT_INPUT: valid owner and dedicated item-data key required.");
        if (_working) return FailScan(DeliveryReceiptCaptureStatus.Unavailable, "EZAPI_RECEIPT_REENTRY: recursive capture/stamp refused.");
        _working = true;
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown) return FailScan(DeliveryReceiptCaptureStatus.Unavailable, "EZAPI_RECEIPT_BUILD: supported game build required.");
            var first = ReadPass(); var second = ReadPass();
            if (!NativeAccess.SameRun(run) || !first.SequenceEqual(second))
                return FailScan(DeliveryReceiptCaptureStatus.Unavailable, "EZAPI_RECEIPT_STALE: observed inventory changed during capture.");
            return new(DeliveryReceiptCaptureStatus.Ready, run, ownerId, key, Array.AsReadOnly(second));

            DeliveryReceiptItem[] ReadPass()
            {
                var items = InventoryApi.CaptureRunItems(GameSession.Resolve(run));
                if (items.Count > DeliveryReceiptAudit.MaximumItems) throw new ReceiptScanException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                var result = new List<DeliveryReceiptItem>(items.Count); long jsonCharacters = 0;
                foreach (var item in items)
                {
                    if (!NativeAccess.SameRun(run)) throw new ReceiptScanException(DeliveryReceiptCaptureStatus.Unavailable, "STALE");
                    var before = InventoryChecks.Item(item, false); var parent = item.parentInventory?.Pointer.ToInt64() ?? 0;
                    var read = ItemDataApi.Read<DeliveryReceipt>(item, ownerId, key);
                    jsonCharacters += read.Json?.Length ?? 0;
                    if (jsonCharacters > MaximumJsonCharacters) throw new ReceiptScanException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                    if (read.Status is not (ItemDataStatus.Missing or ItemDataStatus.Present) ||
                        read.Status == ItemDataStatus.Present && !DeliveryReceiptAudit.ValidReceipt(read.Value))
                        throw new ReceiptScanException(read.Status is ItemDataStatus.Invalid or ItemDataStatus.VersionMismatch or ItemDataStatus.Present
                            ? DeliveryReceiptCaptureStatus.Invalid : DeliveryReceiptCaptureStatus.Unavailable, "TAG");
                    if (!NativeAccess.SameRun(run) || InventoryChecks.Item(item, false) != before || (item.parentInventory?.Pointer.ToInt64() ?? 0) != parent)
                        throw new ReceiptScanException(DeliveryReceiptCaptureStatus.Unavailable, "STALE");
                    result.Add(new(before, parent, read.Value));
                }
                return result.OrderBy(i => i.Item.NativeIdentity).ToArray();
            }
        }
        catch (ReceiptScanException error) { return FailScan(error.Status, "EZAPI_RECEIPT_" + error.Message + ": no partial capture returned."); }
        catch (Exception error) { return FailScan(DeliveryReceiptCaptureStatus.Unavailable, "EZAPI_RECEIPT_SCAN: " + error.GetType().Name + "; no partial capture returned."); }
        finally { _working = false; }
    }

    private sealed class ReceiptScanException : Exception
    {
        internal DeliveryReceiptCaptureStatus Status { get; }
        internal ReceiptScanException(DeliveryReceiptCaptureStatus status, string code) : base(code) => Status = status;
    }
    private static DeliveryReceiptStampResult Fail(ItemDataStatus status, string code, string? detail = null)
        => new(status, Reason: "EZAPI_RECEIPT_" + code + ": receipt stamp unavailable" + (detail is null ? "." : "; " + detail + "."));
}
