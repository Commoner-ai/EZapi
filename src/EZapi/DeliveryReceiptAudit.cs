using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>Stable caller-generated operation ID and the exact whole item/stack expected. Persist before attempting delivery.</summary>
public sealed record DeliveryReceipt(string OperationId, string ItemId, int Units = 1);

/// <summary>Caller ledger state. Pending means no native handoff has ever been attempted; InFlight must never be reset from absence alone.</summary>
public enum DeliveryLedgerState { Pending, InFlight, Delivered }

/// <summary>CanAttempt is a preflight observation only. Observed is inventory evidence, Recorded is the caller's completed ledger.</summary>
public enum DeliveryReceiptDecision { CanAttempt, Observed, Recorded, Conflict, Indeterminate, Invalid }

/// <summary>Ready means a complete bounded capture, never a partial or failed scan.</summary>
public enum DeliveryReceiptCaptureStatus { Ready, Invalid, Unavailable, LimitExceeded }

/// <summary>Managed item identity/count/ownership/parent and this owner's receipt. No native wrappers, geometry or arbitrary item tags.</summary>
public sealed record DeliveryReceiptItem(NativeInventoryItem Item, long ParentIdentity, DeliveryReceipt? Receipt);

/// <summary>Synchronous inventory evidence; not a durable ledger or authenticated proof. Do not mutate Items during analysis.</summary>
public sealed record DeliveryReceiptSnapshot(DeliveryReceiptCaptureStatus Status, GameRunIdentity? Run,
    string OwnerId, string Key, IReadOnlyList<DeliveryReceiptItem> Items, string? Reason = null);

/// <summary>Detached-item identity captured before native handoff; useful only in the same load/Epoch. Never serialize native identities.</summary>
public sealed record DeliveryReceiptStamp(GameRunIdentity Run, string OwnerId, string Key,
    NativeInventoryItem Item, DeliveryReceipt Receipt);

/// <summary>No decision saves a ledger, charges cash, issues/reissues an item or authorizes touching the old native wrapper.</summary>
public sealed record DeliveryReceiptResult(DeliveryReceiptDecision Decision, int Matches = 0,
    DeliveryReceiptItem? Item = null, string? Reason = null);

/// <summary>Pure managed reconciliation. Token absence after handoff is always uncertain, even when captured inventory is unchanged.</summary>
public static class DeliveryReceiptAudit
{
    public const int MaximumItems = 4096;

    /// <summary>Reconcile a stable operation with a fresh scan. Delivered remains recorded when the player has consumed or sold the item.</summary>
    public static DeliveryReceiptResult Reconcile(DeliveryReceiptSnapshot snapshot, DeliveryReceipt receipt, DeliveryLedgerState ledgerState)
    {
        if (!ValidReceipt(receipt) || !Enum.IsDefined(typeof(DeliveryLedgerState), ledgerState)) return Invalid();
        // The authoritative completed ledger prevents re-awarding even if inventory cannot currently be scanned.
        if (ledgerState == DeliveryLedgerState.Delivered) return new(DeliveryReceiptDecision.Recorded);
        if (!TryItems(snapshot, out var items, out var failure)) return failure!;
        var matches = items.Where(i => i.Receipt?.OperationId == receipt.OperationId).ToArray();
        if (matches.Length == 0) return ledgerState == DeliveryLedgerState.Pending
            ? new(DeliveryReceiptDecision.CanAttempt)
            : Uncertain("EZAPI_RECEIPT_ABSENT: attempted delivery has no current receipt; do not reissue automatically.");
        if (matches.Length != 1 || !Matches(matches[0], receipt))
            return new(DeliveryReceiptDecision.Conflict, matches.Length, Reason: "EZAPI_RECEIPT_CONFLICT: duplicate token, wrong item/count, detached or non-player-owned result.");
        return new(DeliveryReceiptDecision.Observed, 1, matches[0]);
    }

    /// <summary>
    /// Verify exactly one whole detached item entered a freshly scanned run, retaining its identity and receipt, with pre-existing observed fields unchanged.
    /// requiredInventoryIdentity optionally requires one exact target in this load. Native return values/exceptions are not evidence.
    /// </summary>
    public static DeliveryReceiptResult CheckHandoff(DeliveryReceiptSnapshot before, DeliveryReceiptSnapshot after,
        DeliveryReceiptStamp stamp, long? requiredInventoryIdentity = null)
    {
        if (stamp is null || !ValidRun(stamp.Run) || !ValidAddress(stamp.OwnerId, stamp.Key) ||
            !ValidReceipt(stamp.Receipt) || !ValidItem(stamp.Item) || !stamp.Item.PlayerOwned ||
            stamp.Item.ItemId != stamp.Receipt.ItemId || stamp.Item.Units != stamp.Receipt.Units || requiredInventoryIdentity == 0)
            return Invalid();
        if (!TryItems(before, out var oldItems, out var failure) || !TryItems(after, out var newItems, out failure)) return failure!;
        if (before.Run != stamp.Run || after.Run != stamp.Run || before.OwnerId != stamp.OwnerId || after.OwnerId != stamp.OwnerId ||
            before.Key != stamp.Key || after.Key != stamp.Key) return Uncertain("EZAPI_RECEIPT_STALE: owner/key or load identity differs.");
        if (oldItems.Any(i => i.Item.NativeIdentity == stamp.Item.NativeIdentity || i.Item.InstanceId == stamp.Item.InstanceId ||
            i.Receipt?.OperationId == stamp.Receipt.OperationId))
            return new(DeliveryReceiptDecision.Conflict, Reason: "EZAPI_RECEIPT_BEFORE: detached identity or operation already present before handoff.");
        var observed = Reconcile(after, stamp.Receipt, DeliveryLedgerState.InFlight);
        if (observed.Decision != DeliveryReceiptDecision.Observed) return observed;
        if (observed.Item!.Item != stamp.Item || requiredInventoryIdentity is {} target && observed.Item.ParentIdentity != target ||
            newItems.Length != oldItems.Length + 1) return Uncertain("EZAPI_RECEIPT_DELTA: exact item identity, target or item count differs.");
        var byIdentity = newItems.ToDictionary(i => i.Item.NativeIdentity);
        if (oldItems.Any(old => !byIdentity.TryGetValue(old.Item.NativeIdentity, out var current) || current != old))
            return Uncertain("EZAPI_RECEIPT_DELTA: pre-existing observed inventory fields changed.");
        return observed;
    }

    internal static bool ValidAddress(string owner, string key) => owner is not null && owner.Length is >= 1 and <= 160 &&
        Regex.IsMatch(owner, @"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z") && key is not null && key.Length is >= 1 and <= 120 &&
        Regex.IsMatch(key, @"^[A-Za-z0-9_.-]+\z");
    internal static bool ValidReceipt(DeliveryReceipt? receipt) => receipt is not null && receipt.OperationId is not null &&
        receipt.OperationId.Length is >= 1 and <= 128 && Regex.IsMatch(receipt.OperationId, @"^[A-Za-z0-9_.:-]+\z") &&
        ValidId(receipt.ItemId) && receipt.Units > 0;
    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 160 && !id.Any(char.IsControl);
    private static bool ValidRun(GameRunIdentity? run) => run is not null && run.SaveSlotId >= 0 && !string.IsNullOrWhiteSpace(run.RunId) && run.Epoch > 0;
    private static bool ValidItem(NativeInventoryItem? item) => item is not null && item.NativeIdentity != 0 &&
        ValidId(item.ItemId) && item.Units > 0 && item.Shape is null;
    private static bool Matches(DeliveryReceiptItem item, DeliveryReceipt receipt) => item.Receipt == receipt &&
        item.Item.ItemId == receipt.ItemId && item.Item.Units == receipt.Units && item.Item.PlayerOwned && item.ParentIdentity != 0;
    private static bool TryItems(DeliveryReceiptSnapshot? snapshot, out DeliveryReceiptItem[] items, out DeliveryReceiptResult? failure)
    {
        items = Array.Empty<DeliveryReceiptItem>(); failure = Invalid();
        if (snapshot is null || !ValidAddress(snapshot.OwnerId, snapshot.Key)) return false;
        if (snapshot.Status != DeliveryReceiptCaptureStatus.Ready)
        { failure = Uncertain("EZAPI_RECEIPT_SCAN: complete current scan required; " + snapshot.Status); return false; }
        if (!ValidRun(snapshot.Run) || snapshot.Items is null || snapshot.Items.Count > MaximumItems) return false;
        items = snapshot.Items.ToArray();
        if (items.Length > MaximumItems || items.Any(i => i is null || !ValidItem(i.Item) || i.ParentIdentity == i.Item.NativeIdentity ||
            i.Receipt is not null && !ValidReceipt(i.Receipt)) ||
            items.Select(i => i.Item.NativeIdentity).Distinct().Count() != items.Length ||
            items.Select(i => i.Item.InstanceId).Distinct().Count() != items.Length) return false;
        failure = null; return true;
    }
    private static DeliveryReceiptResult Invalid() => new(DeliveryReceiptDecision.Invalid, Reason: "EZAPI_RECEIPT_INPUT: valid bounded receipt, snapshot and identities required.");
    private static DeliveryReceiptResult Uncertain(string reason) => new(DeliveryReceiptDecision.Indeterminate, Reason: reason);
}
