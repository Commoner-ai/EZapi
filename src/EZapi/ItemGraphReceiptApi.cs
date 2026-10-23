using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>Fresh main-thread managed receipt graph captures. No retained native objects, graph edits, handoff, retry or save.</summary>
public static class ItemGraphReceiptApi
{
    private static bool _reading;

    /// <summary>Complete current-run item graph, including main/save-bag inventories so an empty destination remains observable.</summary>
    public static DeliveryGraphSnapshot CaptureRun(string ownerId, string key) => Capture(null, ownerId, key);

    /// <summary>
    /// Capture one detached owned root already stamped with ItemReceiptApi.StampDetached, including all descendants/edges.
    /// Only before native handoff; never pass an old wrapper after attempting admission. No tags/ownership are changed here.
    /// </summary>
    public static DeliveryGraphSnapshot CaptureDetached(GameItem root, string ownerId, string key)
        => root is null ? Failed(ownerId, key, DeliveryReceiptCaptureStatus.Invalid, "INPUT") : Capture(root, ownerId, key);

    private static DeliveryGraphSnapshot Capture(GameItem? root, string owner, string key)
    {
        if (!GameSession.OnThread) return Failed(owner, key, DeliveryReceiptCaptureStatus.Unavailable, "THREAD");
        if (!DeliveryReceiptAudit.ValidAddress(owner, key)) return Failed(owner, key, DeliveryReceiptCaptureStatus.Invalid, "INPUT");
        if (_reading) return Failed(owner, key, DeliveryReceiptCaptureStatus.Unavailable, "REENTRY");
        _reading = true;
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown) return Failed(owner, key, DeliveryReceiptCaptureStatus.Unavailable, "BUILD");
            var rootId = root?.Pointer.ToInt64() ?? 0;
            if (root is not null && rootId == 0) return Failed(owner, key, DeliveryReceiptCaptureStatus.Invalid, "INPUT");
            var first = Pass(); var second = Pass();
            if (!NativeAccess.SameRun(run) || !DeliveryGraphAudit.SameObservation(first, second))
                return Failed(owner, key, DeliveryReceiptCaptureStatus.Unavailable, "STALE");
            return second;

            DeliveryGraphSnapshot Pass()
            {
                if (!NativeAccess.SameRun(run)) throw new CaptureException(DeliveryReceiptCaptureStatus.Unavailable, "STALE");
                var queue = new Queue<GraphNodeStorage>();
                if (root is not null) queue.Enqueue(root.TryCast<GraphNodeStorage>()!);
                else
                {
                    var store = GameSession.Resolve(run);
                    foreach (var item in InventoryApi.CaptureRunItems(store)) queue.Enqueue(item.TryCast<GraphNodeStorage>()!);
                    if (store.gridInv is {} main) queue.Enqueue(main.TryCast<GraphNodeStorage>()!);
                    if (store.saveBags is {} bags)
                    {
                        if (bags.Count > DeliveryGraphAudit.MaximumItems) throw new CaptureException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                        foreach (var bag in bags) queue.Enqueue(bag.Value.TryCast<GraphNodeStorage>()!);
                    }
                }
                var visited = new HashSet<long>(); var nodes = new List<DeliveryGraphNode>(); var items = new List<DeliveryReceiptItem>();
                long references = 0, jsonCharacters = 0;
                while (queue.Count > 0)
                {
                    if (!NativeAccess.SameRun(run)) throw new CaptureException(DeliveryReceiptCaptureStatus.Unavailable, "STALE");
                    var node = queue.Dequeue();
                    if (node is null || node.Pointer == IntPtr.Zero) throw new CaptureException(DeliveryReceiptCaptureStatus.Invalid, "INPUT");
                    var id = node.Pointer.ToInt64(); if (!visited.Add(id)) continue;
                    if (visited.Count > DeliveryGraphAudit.MaximumNodes) throw new CaptureException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                    var parents = node.parents; var children = node.children;
                    if ((references += (long)(parents?.Count ?? 0) + (children?.Count ?? 0)) > DeliveryGraphAudit.MaximumReferences)
                        throw new CaptureException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                    var pp = new List<long>(); var cc = new List<long>();
                    if (parents is not null) foreach (var parent in parents)
                    { if (parent is null || parent.Pointer == IntPtr.Zero) throw new CaptureException(DeliveryReceiptCaptureStatus.Invalid, "INPUT"); pp.Add(parent.Pointer.ToInt64()); }
                    if (children is not null) foreach (var child in children)
                    { if (child is null || child.Pointer == IntPtr.Zero) throw new CaptureException(DeliveryReceiptCaptureStatus.Invalid, "INPUT"); cc.Add(child.Pointer.ToInt64()); queue.Enqueue(child); }
                    nodes.Add(new(id, node.TryCast<GameInventory>() is not null, Array.AsReadOnly(pp.OrderBy(p => p).ToArray()), Array.AsReadOnly(cc.OrderBy(c => c).ToArray())));
                    if (node.TryCast<GameItem>() is not {} item) continue;
                    if (items.Count >= DeliveryGraphAudit.MaximumItems) throw new CaptureException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                    var before = InventoryChecks.Item(item, false); var parentId = item.parentInventory?.Pointer.ToInt64() ?? 0;
                    var receipt = ItemDataApi.Read<DeliveryReceipt>(item, owner, key);
                    if ((jsonCharacters += receipt.Json?.Length ?? 0) > ItemReceiptApi.MaximumJsonCharacters)
                        throw new CaptureException(DeliveryReceiptCaptureStatus.LimitExceeded, "LIMIT");
                    if (receipt.Status is not (ItemDataStatus.Missing or ItemDataStatus.Present))
                        throw new CaptureException(receipt.Status is ItemDataStatus.Invalid or ItemDataStatus.VersionMismatch
                            ? DeliveryReceiptCaptureStatus.Invalid : DeliveryReceiptCaptureStatus.Unavailable, "RECEIPT");
                    if (!NativeAccess.SameRun(run) || before != InventoryChecks.Item(item, false) || parentId != (item.parentInventory?.Pointer.ToInt64() ?? 0))
                        throw new CaptureException(DeliveryReceiptCaptureStatus.Unavailable, "STALE");
                    items.Add(new(before, parentId, receipt.Value));
                }
                var result = new DeliveryGraphSnapshot(DeliveryReceiptCaptureStatus.Ready, run, owner, key, rootId,
                    Array.AsReadOnly(nodes.OrderBy(n => n.NativeIdentity).ToArray()), Array.AsReadOnly(items.OrderBy(i => i.Item.NativeIdentity).ToArray()));
                if (!DeliveryGraphAudit.Valid(result, root is not null, out _, out _, out _))
                    throw new CaptureException(DeliveryReceiptCaptureStatus.Invalid, "INPUT");
                return result;
            }
        }
        catch (CaptureException error) { return Failed(owner, key, error.Status, error.Message); }
        catch (Exception error) { return Failed(owner, key, DeliveryReceiptCaptureStatus.Unavailable, "SCAN", error.GetType().Name); }
        finally { _reading = false; }
    }

    private sealed class CaptureException : Exception
    {
        internal DeliveryReceiptCaptureStatus Status { get; }
        internal CaptureException(DeliveryReceiptCaptureStatus status, string code) : base(code) => Status = status;
    }
    private static DeliveryGraphSnapshot Failed(string owner, string key, DeliveryReceiptCaptureStatus status, string code, string? detail = null)
        => new(status, null, owner, key, 0, Array.Empty<DeliveryGraphNode>(), Array.Empty<DeliveryReceiptItem>(),
            "EZAPI_DELIVERY_GRAPH_" + code + ": no partial capture returned" + (detail is null ? "." : "; " + detail + "."));
}
