namespace EZapi;

/// <summary>Managed graph edges, not reusable native pointers. IsInventory distinguishes a legal root handoff target.</summary>
public sealed record DeliveryGraphNode(long NativeIdentity, bool IsInventory,
    IReadOnlyList<long> Parents, IReadOnlyList<long> Children);

/// <summary>RootIdentity=0 for a full current-run capture; nonzero for one detached receipt-bearing root and its complete descendants.</summary>
public sealed record DeliveryGraphSnapshot(DeliveryReceiptCaptureStatus Status, GameRunIdentity? Run, string OwnerId,
    string Key, long RootIdentity, IReadOnlyList<DeliveryGraphNode> Nodes, IReadOnlyList<DeliveryReceiptItem> Items,
    string? Reason = null);

/// <summary>Pure managed whole-graph handoff audit. No graph edits, native object access, retry, ledger or disk save.</summary>
public static class DeliveryGraphAudit
{
    public const int MaximumNodes = 8192, MaximumItems = 4096, MaximumReferences = 16384;

    /// <summary>
    /// Recover the root receipt in a complete run graph. This confirms the current root receipt only, not historical child contents.
    /// Persist an expected content manifest in the caller ledger when cold-load child-content verification is required.
    /// </summary>
    public static DeliveryReceiptResult Reconcile(DeliveryGraphSnapshot snapshot, DeliveryReceipt receipt, DeliveryLedgerState ledgerState)
    {
        if (!DeliveryReceiptAudit.ValidReceipt(receipt) || !Enum.IsDefined(typeof(DeliveryLedgerState), ledgerState)) return Invalid();
        if (ledgerState == DeliveryLedgerState.Delivered) return new(DeliveryReceiptDecision.Recorded);
        if (!Valid(snapshot, false, out _, out _, out var error)) return error!;
        var found = snapshot.Items.Where(i => i.Receipt?.OperationId == receipt.OperationId).ToArray();
        if (found.Length == 0) return ledgerState == DeliveryLedgerState.Pending ? new(DeliveryReceiptDecision.CanAttempt)
            : Uncertain("ABSENT");
        if (found.Length != 1 || found[0].Receipt != receipt || !found[0].Item.PlayerOwned || found[0].ParentIdentity == 0 ||
            found[0].Item.ItemId != receipt.ItemId || found[0].Item.Units != receipt.Units)
            return new(DeliveryReceiptDecision.Conflict, found.Length, Reason: "EZAPI_DELIVERY_GRAPH_RECEIPT: duplicate or mismatched root receipt.");
        return new(DeliveryReceiptDecision.Observed, 1, found[0]);
    }

    /// <summary>
    /// Require exactly the detached graph to enter one existing inventory, no other observed changes, same epoch/address.
    /// Default preserves every child's ownership/instance ID. Explicit allowChildOwnershipInitialization permits only unowned-to-owned
    /// child initialization (and its instance ID change); identity/ID/units/parent/receipt/internal edges must still match.
    /// </summary>
    public static DeliveryReceiptResult CheckHandoff(DeliveryGraphSnapshot before, DeliveryGraphSnapshot after,
        DeliveryGraphSnapshot detached, long? requiredInventoryIdentity = null, bool allowChildOwnershipInitialization = false)
    {
        if (requiredInventoryIdentity == 0) return Invalid();
        if (!Valid(before, false, out var oldNodes, out var oldItems, out var error) ||
            !Valid(after, false, out var newNodes, out var newItems, out error) ||
            !Valid(detached, true, out var detachedNodes, out var detachedItems, out error)) return error!;
        if (before.Run != detached.Run || after.Run != detached.Run || before.OwnerId != detached.OwnerId || after.OwnerId != detached.OwnerId ||
            before.Key != detached.Key || after.Key != detached.Key) return Uncertain("STALE");
        var root = detachedItems[detached.RootIdentity];
        var receipt = root.Receipt!;
        if (oldNodes.Keys.Intersect(detachedNodes.Keys).Any() || oldItems.Values.Any(i => i.Receipt?.OperationId == receipt.OperationId))
            return new(DeliveryReceiptDecision.Conflict, Reason: "EZAPI_DELIVERY_GRAPH_BEFORE: borrowed nodes or operation already present before handoff.");
        var observed = Reconcile(after, receipt, DeliveryLedgerState.InFlight);
        if (observed.Decision != DeliveryReceiptDecision.Observed) return observed;
        if (observed.Item!.Item.NativeIdentity != detached.RootIdentity) return Uncertain("DELTA");
        var target = observed.Item.ParentIdentity;
        if (requiredInventoryIdentity is {} required && target != required || !oldNodes.TryGetValue(target, out var targetNode) || !targetNode.IsInventory)
            return Uncertain("TARGET");
        if (newNodes.Count != oldNodes.Count + detachedNodes.Count || newItems.Count != oldItems.Count + detachedItems.Count)
            return Uncertain("DELTA");
        foreach (var old in oldNodes.Values)
        {
            if (!newNodes.TryGetValue(old.NativeIdentity, out var current) || old.IsInventory != current.IsInventory || !Same(old.Parents, current.Parents) ||
                !Same(old.NativeIdentity == target ? old.Children.Append(detached.RootIdentity).ToArray() : old.Children, current.Children)) return Uncertain("DELTA");
        }
        foreach (var entry in oldItems)
            if (!newItems.TryGetValue(entry.Key, out var current) || current != entry.Value) return Uncertain("DELTA");
        foreach (var node in detachedNodes.Values)
        {
            if (!newNodes.TryGetValue(node.NativeIdentity, out var current) || node.IsInventory != current.IsInventory || !Same(node.Children, current.Children) ||
                !Same(node.NativeIdentity == detached.RootIdentity ? new[] { target } : node.Parents, current.Parents)) return Uncertain("DELTA");
        }
        foreach (var entry in detachedItems)
        {
            if (!newItems.TryGetValue(entry.Key, out var current)) return Uncertain("DELTA");
            var original = entry.Value;
            if (entry.Key == detached.RootIdentity)
            { if (current != original with { ParentIdentity = target }) return Uncertain("DELTA"); }
            else if (current != original)
            {
                if (!allowChildOwnershipInitialization || original.Item.PlayerOwned || !current.Item.PlayerOwned ||
                    current != original with { Item = original.Item with { PlayerOwned = true, InstanceId = current.Item.InstanceId } }) return Uncertain("DELTA");
            }
        }
        return observed;
    }

    /// <summary>Exact managed observed fields, independent of list ordering. Invalid/unavailable snapshots never compare equal.</summary>
    public static bool SameObservation(DeliveryGraphSnapshot first, DeliveryGraphSnapshot second)
    {
        if (first is null || second is null || first.RootIdentity != second.RootIdentity ||
            !Valid(first, first.RootIdentity != 0, out var a, out var ai, out _) ||
            !Valid(second, second.RootIdentity != 0, out var b, out var bi, out _) || first.Run != second.Run ||
            first.OwnerId != second.OwnerId || first.Key != second.Key || a.Count != b.Count || ai.Count != bi.Count) return false;
        return a.Values.All(n => b.TryGetValue(n.NativeIdentity, out var other) && n.IsInventory == other.IsInventory &&
            Same(n.Parents, other.Parents) && Same(n.Children, other.Children)) && ai.All(i => bi.TryGetValue(i.Key, out var current) && i.Value == current);
    }

    internal static bool Valid(DeliveryGraphSnapshot? snapshot, bool detached, out Dictionary<long, DeliveryGraphNode> nodes,
        out Dictionary<long, DeliveryReceiptItem> items, out DeliveryReceiptResult? error)
    {
        nodes = new(); items = new(); error = Invalid();
        if (snapshot is null || !DeliveryReceiptAudit.ValidAddress(snapshot.OwnerId, snapshot.Key)) return false;
        if (snapshot.Status != DeliveryReceiptCaptureStatus.Ready) { error = Uncertain("SCAN"); return false; }
        if (snapshot.Run is not { SaveSlotId: >= 0, Epoch: > 0 } run || string.IsNullOrWhiteSpace(run.RunId) ||
            detached != (snapshot.RootIdentity != 0) || snapshot.Nodes is null || snapshot.Items is null ||
            snapshot.Nodes.Count > MaximumNodes || snapshot.Items.Count > MaximumItems) return false;
        long references = 0;
        foreach (var node in snapshot.Nodes)
        {
            if (node is null || node.NativeIdentity == 0 || node.Parents is null || node.Children is null ||
                node.Parents.Count > MaximumReferences || node.Children.Count > MaximumReferences ||
                (references += (long)node.Parents.Count + node.Children.Count) > MaximumReferences ||
                node.Parents.Any(p => p == 0 || p == node.NativeIdentity) || node.Children.Any(c => c == 0 || c == node.NativeIdentity) ||
                node.Parents.Distinct().Count() != node.Parents.Count || node.Children.Distinct().Count() != node.Children.Count || !nodes.TryAdd(node.NativeIdentity, node)) return false;
        }
        var parentsByNode = nodes.ToDictionary(n => n.Key, n => n.Value.Parents.ToHashSet());
        var childrenByNode = nodes.ToDictionary(n => n.Key, n => n.Value.Children.ToHashSet());
        foreach (var item in snapshot.Items)
        {
            if (item?.Item is not {} value || value.NativeIdentity == 0 || value.Units < 1 || value.Shape is not null ||
                string.IsNullOrWhiteSpace(value.ItemId) || value.ItemId.Length > 160 || value.ItemId.Any(char.IsControl) ||
                item.Receipt is not null && !DeliveryReceiptAudit.ValidReceipt(item.Receipt) || !nodes.TryGetValue(value.NativeIdentity, out var node) || node.IsInventory ||
                item.ParentIdentity != 0 && (!nodes.TryGetValue(item.ParentIdentity, out var parent) || !parent.IsInventory || !parentsByNode[value.NativeIdentity].Contains(item.ParentIdentity)) ||
                !items.TryAdd(value.NativeIdentity, item)) return false;
        }
        // Unowned detached children can have duplicate native instance ID0. Owned identities remain unique.
        var owned = items.Values.Where(i => i.Item.PlayerOwned).Select(i => i.Item.InstanceId).ToArray();
        if (owned.Distinct().Count() != owned.Length) return false;
        foreach (var node in nodes.Values)
        {
            foreach (var child in node.Children)
                if (!parentsByNode.TryGetValue(child, out var next) || !next.Contains(node.NativeIdentity)) return false;
            foreach (var parent in node.Parents)
                if (childrenByNode.TryGetValue(parent, out var previous) ? !previous.Contains(node.NativeIdentity) : detached) return false;
        }
        var localNodes = nodes;
        var degree = nodes.ToDictionary(n => n.Key, n => n.Value.Parents.Count(p => localNodes.ContainsKey(p)));
        var ready = new Queue<long>(degree.Where(d => d.Value == 0).Select(d => d.Key)); var visited = 0;
        while (ready.Count > 0) { var id = ready.Dequeue(); visited++; foreach (var child in nodes[id].Children) if (--degree[child] == 0) ready.Enqueue(child); }
        if (visited != nodes.Count) return false;
        if (detached)
        {
            if (!nodes.TryGetValue(snapshot.RootIdentity, out var root) || root.IsInventory || root.Parents.Count != 0 ||
                !items.TryGetValue(root.NativeIdentity, out var rootItem) || !rootItem.Item.PlayerOwned || rootItem.ParentIdentity != 0 ||
                rootItem.Receipt is not {} receipt || receipt.ItemId != rootItem.Item.ItemId || receipt.Units != rootItem.Item.Units) return false;
            var found = new HashSet<long>(); var todo = new Queue<long>(); todo.Enqueue(root.NativeIdentity);
            while (todo.Count > 0) { var id = todo.Dequeue(); if (!found.Add(id)) continue; foreach (var child in nodes[id].Children) todo.Enqueue(child); }
            if (found.Count != nodes.Count || items.Values.Count(i => i.Receipt?.OperationId == receipt.OperationId) != 1) return false;
        }
        error = null; return true;
    }
    private static bool Same(IReadOnlyList<long> a, IReadOnlyList<long> b) => a.Count == b.Count && a.OrderBy(i => i).SequenceEqual(b.OrderBy(i => i));
    private static DeliveryReceiptResult Invalid() => new(DeliveryReceiptDecision.Invalid, Reason: "EZAPI_DELIVERY_GRAPH_INPUT: complete bounded graph/receipt evidence required.");
    private static DeliveryReceiptResult Uncertain(string code) => new(DeliveryReceiptDecision.Indeterminate,
        Reason: "EZAPI_DELIVERY_GRAPH_" + code + ": handoff not confirmed; never automatically reissue or access the old wrapper.");
}
