using EZapi;
using Il2Cpp;
namespace Author.GraphDeliveries;

public sealed record GraphAttemptResult(bool NativeHandoffAttempted, DeliveryReceiptResult Audit, string? NativeError = null);

/// <summary>Consumer workflow for one already-created filled reward. Caller owns ledger, payment and disk saving.</summary>
public static class GraphDelivery
{
    public const string Owner = "author.graph-deliveries", Key = "receipts";

    /// <summary>Only the current root receipt is recovered. Persist a separate expected manifest for cold-load child checks.</summary>
    public static DeliveryReceiptResult Recover(DeliveryReceipt receipt, DeliveryLedgerState state)
        => DeliveryGraphAudit.Reconcile(ItemGraphReceiptApi.CaptureRun(Owner, Key), receipt, state);

    /// <summary>
    /// Use a stable never-attempted receipt and a current main-thread callback. stageInFlight must compare/read back the caller's
    /// ledger for this run and return true only for confirmed in-memory staging. This does not prove disk persistence.
    /// After NativeHandoffAttempted=true, never read/destroy/reuse detached or its descendants, even after an exception.
    /// Child ownership initialization is an explicit caller policy, default false.
    /// </summary>
    public static GraphAttemptResult AttemptOnce(GameItem detached, GameGridInventory target, DeliveryReceipt receipt,
        DeliveryLedgerState state, Func<bool> stageInFlight, bool allowChildOwnershipInitialization = false)
    {
        var attempted = false;
        try
        {
            var before = ItemGraphReceiptApi.CaptureRun(Owner, Key);
            var ready = DeliveryGraphAudit.Reconcile(before, receipt, state);
            if (ready.Decision != DeliveryReceiptDecision.CanAttempt) return new(false, ready);
            if (ItemReceiptApi.StampDetached(detached, Owner, Key, receipt).Stamp is null) return Stop("Receipt staging unconfirmed.");
            var graph = ItemGraphReceiptApi.CaptureDetached(detached, Owner, Key);
            if (graph.Status != DeliveryReceiptCaptureStatus.Ready || graph.Run != before.Run) return Stop("Detached graph unavailable.");
            var marker = target.TryFindOneValidInventorySlot(detached, false);
            bool ValidSlot() => marker is not null && marker.Pointer != IntPtr.Zero && marker.IsValid() &&
                marker.item?.Pointer == detached.Pointer && marker.inventory?.Pointer == target.Pointer &&
                marker.targetItem is null && marker.itemGridShape is not null && marker.numTransfer >= receipt.Units &&
                !target.IsInsertLocked() && GeneralHelper.MayPlayerInsertInto(target) && GeneralHelper.MayOwnershipAcceptItem(detached, target);
            if (!ValidSlot()) return Stop("No empty whole-item slot.");
            var targetIdentity = target.Pointer.ToInt64();
            if (!before.Nodes.Any(n => n.NativeIdentity == targetIdentity && n.IsInventory)) return Stop("Target outside observed run graph.");
            bool SamePreflight() => DeliveryGraphAudit.SameObservation(before, ItemGraphReceiptApi.CaptureRun(Owner, Key)) &&
                DeliveryGraphAudit.SameObservation(graph, ItemGraphReceiptApi.CaptureDetached(detached, Owner, Key));
            // Slot queries and caller ledger callbacks may alter the graph; check both sides after each.
            if (!SamePreflight()) return Stop("Graph changed before ledger staging.");
            if (!stageInFlight()) return Stop("Ledger staging unconfirmed; reconcile before any retry.");
            if (!ValidSlot() || !SamePreflight()) return Stop("Graph/slot changed after ledger staging; preserve InFlight.");
            string? nativeError = null;
            attempted = true;
            try { marker!.TryAcceptOnce(receipt.Units); }
            catch (Exception error) { nativeError = error.GetType().Name; }
            // After admission starts, use managed snapshots and freshly resolved native objects only.
            var after = ItemGraphReceiptApi.CaptureRun(Owner, Key);
            return new(true, DeliveryGraphAudit.CheckHandoff(before, after, graph, targetIdentity, allowChildOwnershipInitialization), nativeError);
        }
        catch (Exception error) { return Stop("Unconfirmed graph workflow: " + error.GetType().Name); }

        GraphAttemptResult Stop(string reason) => new(attempted, new(DeliveryReceiptDecision.Indeterminate, Reason: reason));
    }
}
