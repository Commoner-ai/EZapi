using EZapi;
using Il2Cpp;
namespace Author.Deliveries;

public sealed record AttemptResult(bool NativeHandoffAttempted, DeliveryReceiptResult Audit, string? NativeError = null);

/// <summary>Consumer example, not an automatic award system. Persist each stable receipt and own the ledger/save/payment policy.</summary>
public static class ReceiptDelivery
{
    public const string Owner = "author.deliveries", Key = "receipts";

    /// <summary>Call on load/recovery. Recorded/Observed never authorize a new item; inflight absence stays uncertain.</summary>
    public static DeliveryReceiptResult Recover(DeliveryReceipt receipt, DeliveryLedgerState state)
        => DeliveryReceiptAudit.Reconcile(ItemReceiptApi.Capture(Owner, Key), receipt, state);

    /// <summary>
    /// Existing detached item, never attempted token and empty whole-item slot only. stageInFlight must compare/read back the caller's
    /// ledger using RunDataApi and return true only for StagedInMemory; this still does not mean disk persistence.
    /// After NativeHandoffAttempted=true the caller must never inspect/destroy/reuse detached, even if the native method threw.
    /// </summary>
    public static AttemptResult AttemptOnce(GameItem detached, GameGridInventory target, DeliveryReceipt receipt,
        DeliveryLedgerState state, Func<bool> stageInFlight)
    {
        var attempted = false;
        try
        {
            var before = ItemReceiptApi.Capture(Owner, Key);
            var ready = DeliveryReceiptAudit.Reconcile(before, receipt, state);
            if (ready.Decision != DeliveryReceiptDecision.CanAttempt) return new(false, ready);
            var tagged = ItemReceiptApi.StampDetached(detached, Owner, Key, receipt);
            if (tagged.Stamp is not {} stamp) return Stop("Receipt stamp: " + tagged.Status);
            var marker = target.TryFindOneValidInventorySlot(detached, false);
            bool ValidSlot() => marker is not null && marker.Pointer != IntPtr.Zero && marker.IsValid() &&
                marker.item?.Pointer == detached.Pointer && marker.inventory?.Pointer == target.Pointer &&
                marker.targetItem is null && marker.itemGridShape is not null && marker.numTransfer >= receipt.Units &&
                !target.IsInsertLocked() && GeneralHelper.MayPlayerInsertInto(target) && GeneralHelper.MayOwnershipAcceptItem(detached, target);
            if (!ValidSlot()) return Stop("No empty whole-item slot; native handoff not attempted.");
            var targetIdentity = target.Pointer.ToInt64();
            // Both native slot queries and caller ledger staging may run other Mod callbacks.
            var preflight = ItemReceiptApi.Capture(Owner, Key);
            if (!Same(before, preflight)) return Stop("Inventory changed before ledger staging.");
            if (!stageInFlight()) return Stop("Ledger staging not confirmed; do not assume an absent item can be reissued.");
            preflight = ItemReceiptApi.Capture(Owner, Key);
            if (!Same(before, preflight) || !ValidSlot() || ItemReceiptApi.StampDetached(detached, Owner, Key, receipt).Stamp != stamp)
                return Stop("Preflight changed after ledger staging; reconcile the inflight ledger.");
            string? nativeError = null;
            attempted = true;
            try { marker!.TryAcceptOnce(receipt.Units); }
            catch (Exception error) { nativeError = error.GetType().Name; }
            // No access to detached/marker after this handoff: only freshly captured objects.
            var after = ItemReceiptApi.Capture(Owner, Key);
            return new(true, DeliveryReceiptAudit.CheckHandoff(before, after, stamp, targetIdentity), nativeError);
        }
        catch (Exception error) { return Stop("Unconfirmed example workflow: " + error.GetType().Name); }

        AttemptResult Stop(string reason) => new(attempted, new(DeliveryReceiptDecision.Indeterminate, Reason: reason));
    }

    private static bool Same(DeliveryReceiptSnapshot a, DeliveryReceiptSnapshot b) =>
        a.Status == DeliveryReceiptCaptureStatus.Ready && b.Status == DeliveryReceiptCaptureStatus.Ready &&
        a.Run == b.Run && a.OwnerId == b.OwnerId && a.Key == b.Key && a.Items.SequenceEqual(b.Items);
}
