using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>Explicit native prepared factories; distinct from ordinary item-directory templates.</summary>
public enum PreparedItemKind
{
    SecurityCrate, MedicalCrate, EngineeringCrate, ServiceCrate, EvidenceCrate,
    PerformanceModule, EfficiencyModule, QualityModule, OverclockModule, EcoModule, FinenessModule, GenuineInjector
}

/// <summary>Managed recipe route. ItemId is the actual native inventory ID, not an invented spawn alias.</summary>
public sealed record PreparedItemDefinition(PreparedItemKind Kind, string ItemId, string NativeFactory, string? LootRecipe = null);

/// <summary>Created returns a detached object owned by the caller. Indeterminate must not cause an automatic random reroll.</summary>
public enum PreparedItemStatus { Created, Invalid, Unavailable, Conflict, Indeterminate }

/// <summary>DestroyReturned means the native cleanup call returned, not independently verified destruction or rollback of RNG.</summary>
public enum PreparedItemCleanup { None, DestroyReturned, SkippedUnprovenGraph, SkippedStaleRun, Failed, FactoryDidNotReturn }

/// <summary>Managed creation observation; nested counts exclude the root. Does not prove gameplay effects, delivery or disk persistence.</summary>
public sealed record PreparedItemSnapshot(GameRunIdentity Run, PreparedItemDefinition Definition, int InstanceId,
    bool PlayerOwned, int NestedItems);

/// <summary>Only Created exposes Item. FactoryAttempted stays true on every failure after entering the native factory; never blindly retry.</summary>
public sealed record PreparedItemResult(PreparedItemStatus Status, GameItem? Item = null,
    PreparedItemSnapshot? Snapshot = null, bool FactoryAttempted = false,
    PreparedItemCleanup Cleanup = PreparedItemCleanup.None, string? Reason = null);

/// <summary>
/// Native filled crates/random modules/genuine injector, with bounded graph provenance and detached ownership validation.
/// No patches, registration replacement, in-place repair, delivery, payment, random reroll or saving.
/// </summary>
public static class PreparedItemApi
{
    private static readonly IReadOnlyList<PreparedItemDefinition> Definitions = Array.AsReadOnly(new[] {
        new PreparedItemDefinition(PreparedItemKind.SecurityCrate,"sec_box","LootCrateSecurity","loot_crate_security"),
        new PreparedItemDefinition(PreparedItemKind.MedicalCrate,"med_box","LootCrateMedical","loot_crate_medical"),
        new PreparedItemDefinition(PreparedItemKind.EngineeringCrate,"eng_box","LootCrateEngineering","loot_crate_engineering"),
        new PreparedItemDefinition(PreparedItemKind.ServiceCrate,"service_box","LootCrateService","loot_crate_service"),
        new PreparedItemDefinition(PreparedItemKind.EvidenceCrate,"evidence_box","LootCrateEvidence","loot_crate_evidence"),
        new PreparedItemDefinition(PreparedItemKind.PerformanceModule,"system_module_performance","RandomPerformanceModule"),
        new PreparedItemDefinition(PreparedItemKind.EfficiencyModule,"system_module_efficiancy","RandomEfficiencyModule"),
        new PreparedItemDefinition(PreparedItemKind.QualityModule,"system_module_quality","RandomQualityModule"),
        new PreparedItemDefinition(PreparedItemKind.OverclockModule,"system_module_overclock","RandomOverclockModule"),
        new PreparedItemDefinition(PreparedItemKind.EcoModule,"system_module_eco","RandomEcoModule"),
        new PreparedItemDefinition(PreparedItemKind.FinenessModule,"system_module_fineness","RandomFinenessModule"),
        new PreparedItemDefinition(PreparedItemKind.GenuineInjector,"large_purple_injector","CreateRealGenuineInjector")
    });
    private static bool _creating;

    /// <summary>All twelve explicit choices; pure managed lookup, no game/thread/factory required.</summary>
    public static IReadOnlyList<PreparedItemDefinition> Describe() => Definitions;

    /// <summary>
    /// Call exactly one native prepared factory, then initialize a new crate's native runtime lock and requested root ownership.
    /// Requires loaded main thread, known build and freeze gates; successful Item must be natively admitted or safely disposed by caller.
    /// </summary>
    public static PreparedItemResult Create(PreparedItemKind kind, bool owned = true)
    {
        if (!GameSession.OnThread) return Fail(PreparedItemStatus.Unavailable, "THREAD");
        var definition = Definitions.SingleOrDefault(d => d.Kind == kind);
        if (definition is null) return Fail(PreparedItemStatus.Invalid, "KIND");
        if (_creating) return Fail(PreparedItemStatus.Conflict, "REENTRY");
        _creating = true;
        GameRunIdentity? run = null; GameItem? item = null; Graph? before = null;
        var attempted = false;
        try
        {
            run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown || !InventoryFreezeApi.IsAvailable) return Fail(PreparedItemStatus.Unavailable, "BUILD");
            if (InventoryApi.IsItemDragActive) return Fail(PreparedItemStatus.Conflict, "BUSY");
            if (!DirectoryMaster.Has<GameItem>(definition.ItemId)) return Fail(PreparedItemStatus.Unavailable, "MISSING");
            before = CaptureRun(run);
            if (!NativeAccess.SameRun(run)) return Fail(PreparedItemStatus.Conflict, "STALE");
            attempted = true;
            item = Make(kind); // A factory may consume randomness or run other Mod callbacks. Never call it again here.
            RequireRun();
            NativeAccess.RequireItem(item);
            var graph = CaptureGraph(run, new[] { item.TryCast<GraphNodeStorage>()! }, true, before.Nodes);
            RequireItem();
            if (CaptureRun(run).Evidence != before.Evidence) throw new InvalidOperationException("SIDE_EFFECT");
            if (InventoryFreezeApi.IsFrozen(item)) throw new InvalidOperationException("FROZEN");
            RequireItem();
            if (definition.LootRecipe is {} recipe)
            {
                if (graph.Items.Count < 2 || item.GetTagReadonly("LOOT_CRATE_ID_STRING")?.GetString() != recipe)
                    throw new InvalidOperationException("CRATE");
                RequireItem();
                LockHelper.LoadLockedContainer(item); // New objects only; never fill/refill or repair an existing player's crate.
                RequireItem();
                if (!LockHelper.IsCurrentlyLocked(item) || item.contentWindow?.isLocked != true)
                    throw new InvalidOperationException("LOCK");
            }
            RequireItem();
            var ownership = GeneralHelper.IsItemOwned(item);
            RequireItem();
            if (ownership != owned) GeneralHelper.SetItemOwned(item, owned);
            RequireItem();
            item.SyncModifiedState();
            RequireItem();
            var final = CaptureGraph(run, new[] { item.TryCast<GraphNodeStorage>()! }, true, before.Nodes);
            if (final.Structure != graph.Structure || CaptureRun(run).Evidence != before.Evidence || !InventoryFreezeApi.IsAvailable || InventoryFreezeApi.IsFrozen(item))
                throw new InvalidOperationException("SIDE_EFFECT");
            RequireItem();
            ownership = GeneralHelper.IsItemOwned(item);
            RequireItem();
            // Detached unowned children may legitimately share native instance ID0.
            // Do not invent an ownership/ID migration for those child objects.
            var ownedIds = final.Items.Values.Where(i => i.Owned).Select(i => i.InstanceId).ToArray();
            if (ownership != owned || owned && (before.Items.Values.Any(i => i.InstanceId == item.uniqueId) ||
                ownedIds.Distinct().Count() != ownedIds.Length))
                throw new InvalidOperationException("OWNERSHIP");
            if (definition.LootRecipe is {} expected && (item.GetTagReadonly("LOOT_CRATE_ID_STRING")?.GetString() != expected ||
                !LockHelper.IsCurrentlyLocked(item) || item.contentWindow?.isLocked != true)) throw new InvalidOperationException("LOCK");
            RequireItem();
            if (CaptureGraph(run, new[] { item.TryCast<GraphNodeStorage>()! }, true, before.Nodes).Evidence != final.Evidence ||
                CaptureRun(run).Evidence != before.Evidence) throw new InvalidOperationException("SIDE_EFFECT");
            RequireItem();
            return new(PreparedItemStatus.Created, item, new(run, definition, item.uniqueId, ownership, final.Items.Count - 1), true);

            void RequireRun()
            { if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("STALE"); }
            void RequireItem()
            {
                RequireRun(); NativeAccess.RequireItem(item);
                if (item.identifier != definition.ItemId || item.unitCount != 1 || item.parentInventory is not null ||
                    item.state is null || item.state.Pointer == IntPtr.Zero || before.Nodes.Contains(item.Pointer))
                    throw new InvalidOperationException("RESULT");
            }
        }
        catch (Exception error)
        {
            if (!attempted) return Fail(PreparedItemStatus.Unavailable, "PREFLIGHT", error.GetType().Name);
            var cleanup = PreparedItemCleanup.FactoryDidNotReturn;
            if (item is not null)
            {
                cleanup = PreparedItemCleanup.SkippedUnprovenGraph;
                if (run is null || !NativeAccess.SameRun(run)) cleanup = PreparedItemCleanup.SkippedStaleRun;
                else if (before is not null)
                {
                    try
                    {
                        // Reprove the entire current graph: cached ownership/provenance cannot authorize destructive cleanup.
                        var graph = CaptureGraph(run, new[] { item.TryCast<GraphNodeStorage>()! }, true, before.Nodes);
                        if (NativeAccess.SameRun(run) && graph.Nodes.Contains(item.Pointer) && InventoryFreezeApi.IsAvailable &&
                            !InventoryFreezeApi.IsFrozen(item) && NativeAccess.SameRun(run))
                        {
                            var currentRun = CaptureRun(run);
                            var current = CaptureGraph(run, new[] { item.TryCast<GraphNodeStorage>()! }, true, currentRun.Nodes);
                            if (NativeAccess.SameRun(run) && current.Structure == graph.Structure && item.parentInventory is null)
                            { item.Destroy(); cleanup = PreparedItemCleanup.DestroyReturned; }
                        }
                    }
                    catch { cleanup = PreparedItemCleanup.Failed; }
                }
            }
            var stage = error is InvalidOperationException && new[] { "SIDE_EFFECT", "FROZEN", "CRATE", "LOCK", "OWNERSHIP", "STALE", "RESULT", "LIMIT", "GRAPH" }.Contains(error.Message)
                ? error.Message : "CREATE";
            return new(PreparedItemStatus.Indeterminate, FactoryAttempted: true, Cleanup: cleanup,
                Reason: "EZAPI_PREPARED_" + stage + ": " + error.GetType().Name + "; no automatic reroll.");
        }
        finally { _creating = false; }
    }

    private static GameItem Make(PreparedItemKind kind) => kind switch {
        PreparedItemKind.SecurityCrate => PreBuiltItemHelper.LootCrateSecurity(),
        PreparedItemKind.MedicalCrate => PreBuiltItemHelper.LootCrateMedical(),
        PreparedItemKind.EngineeringCrate => PreBuiltItemHelper.LootCrateEngineering(),
        PreparedItemKind.ServiceCrate => PreBuiltItemHelper.LootCrateService(),
        PreparedItemKind.EvidenceCrate => PreBuiltItemHelper.LootCrateEvidence(),
        PreparedItemKind.PerformanceModule => PreBuiltItemHelper.RandomPerformanceModule(),
        PreparedItemKind.EfficiencyModule => PreBuiltItemHelper.RandomEfficiencyModule(),
        PreparedItemKind.QualityModule => PreBuiltItemHelper.RandomQualityModule(),
        PreparedItemKind.OverclockModule => PreBuiltItemHelper.RandomOverclockModule(),
        PreparedItemKind.EcoModule => PreBuiltItemHelper.RandomEcoModule(),
        PreparedItemKind.FinenessModule => PreBuiltItemHelper.RandomFinenessModule(),
        PreparedItemKind.GenuineInjector => PreBuiltItemHelper.CreateRealGenuineInjector(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record ItemState(int InstanceId, string Id, int Units, bool Owned, long Parent);
    private sealed record Graph(HashSet<IntPtr> Nodes, Dictionary<IntPtr, ItemState> Items, string Structure, string Evidence);
    private static Graph CaptureRun(GameRunIdentity run)
    {
        var store = GameSession.Resolve(run);
        var roots = InventoryApi.CaptureRunItems(store).Select(i => i.TryCast<GraphNodeStorage>()!).ToList();
        if (store.gridInv is {} main) roots.Add(main.TryCast<GraphNodeStorage>()!);
        if (store.saveBags is {} bags)
        {
            if (bags.Count > 4096) throw new InvalidOperationException("LIMIT");
            foreach (var bag in bags) roots.Add(bag.Value.TryCast<GraphNodeStorage>()!);
        }
        return CaptureGraph(run, roots, false, null);
    }
    private static Graph CaptureGraph(GameRunIdentity run, IEnumerable<GraphNodeStorage> roots, bool isolated, HashSet<IntPtr>? excluded)
    {
        var nodes = new Dictionary<IntPtr, (long[] Parents, long[] Children)>();
        var items = new Dictionary<IntPtr, ItemState>();
        var queue = new Queue<GraphNodeStorage>(roots); var edges = 0;
        while (queue.Count > 0)
        {
            if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("STALE");
            var node = queue.Dequeue();
            if (node is null || node.Pointer == IntPtr.Zero || excluded?.Contains(node.Pointer) == true)
                throw new InvalidOperationException("GRAPH");
            if (nodes.ContainsKey(node.Pointer)) continue;
            if (nodes.Count >= 8192) throw new InvalidOperationException("LIMIT");
            var parents = node.parents; var children = node.children;
            if ((parents?.Count ?? 0) > 16384 - edges) throw new InvalidOperationException("LIMIT");
            edges += parents?.Count ?? 0;
            if ((children?.Count ?? 0) > 16384 - edges) throw new InvalidOperationException("LIMIT");
            edges += children?.Count ?? 0;
            var pp = new List<long>(); var cc = new List<long>();
            if (parents is not null) foreach (var p in parents)
            { if (p is null || p.Pointer == IntPtr.Zero) throw new InvalidOperationException("GRAPH"); pp.Add(p.Pointer.ToInt64()); }
            if (children is not null) foreach (var c in children)
            { if (c is null || c.Pointer == IntPtr.Zero) throw new InvalidOperationException("GRAPH"); cc.Add(c.Pointer.ToInt64()); queue.Enqueue(c); }
            if (pp.Distinct().Count() != pp.Count || cc.Distinct().Count() != cc.Count) throw new InvalidOperationException("GRAPH");
            nodes.Add(node.Pointer, (pp.OrderBy(p => p).ToArray(), cc.OrderBy(p => p).ToArray()));
            if (node.TryCast<GameItem>() is {} item)
            {
                NativeAccess.RequireItem(item);
                if (items.Count >= 4096 || string.IsNullOrWhiteSpace(item.identifier) || item.identifier.Length > 160 || item.identifier.Any(char.IsControl))
                    throw new InvalidOperationException("LIMIT");
                var id = item.uniqueId; var itemId = item.identifier; var units = item.unitCount;
                var owned = GeneralHelper.IsItemOwned(item);
                if (!NativeAccess.SameRun(run) || item.uniqueId != id || item.identifier != itemId || item.unitCount != units) throw new InvalidOperationException("STALE");
                items.Add(item.Pointer, new(id, itemId, units, owned, item.parentInventory?.Pointer.ToInt64() ?? 0));
            }
        }
        if (!NativeAccess.SameRun(run)) throw new InvalidOperationException("STALE");
        if (isolated)
        {
            var degree = nodes.ToDictionary(n => n.Key.ToInt64(), n => n.Value.Parents.Length);
            foreach (var n in nodes)
            {
                foreach (var p in n.Value.Parents)
                    if (!nodes.TryGetValue(new(p), out var parent) || !parent.Children.Contains(n.Key.ToInt64())) throw new InvalidOperationException("GRAPH");
                foreach (var c in n.Value.Children)
                    if (!nodes.TryGetValue(new(c), out var child) || !child.Parents.Contains(n.Key.ToInt64())) throw new InvalidOperationException("GRAPH");
            }
            var ready = new Queue<long>(degree.Where(d => d.Value == 0).Select(d => d.Key)); var count = 0;
            while (ready.Count > 0) { var p = ready.Dequeue(); count++; foreach (var child in nodes[new(p)].Children) if (--degree[child] == 0) ready.Enqueue(child); }
            if (count != nodes.Count) throw new InvalidOperationException("GRAPH");
        }
        var structure = string.Join(";", nodes.OrderBy(n => n.Key.ToInt64()).Select(n => n.Key.ToInt64() + ":" + string.Join(",", n.Value.Parents) + "/" + string.Join(",", n.Value.Children))) + "|" +
            System.Text.Json.JsonSerializer.Serialize(items.OrderBy(i => i.Key.ToInt64()).Select(i => new { Pointer = i.Key.ToInt64(), i.Value.Id, i.Value.Units, i.Value.Parent }));
        var evidence = structure + "|" + System.Text.Json.JsonSerializer.Serialize(items.OrderBy(i => i.Key.ToInt64()).Select(i => new { Pointer = i.Key.ToInt64(), i.Value }));
        return new(nodes.Keys.ToHashSet(), items, structure, evidence);
    }
    private static PreparedItemResult Fail(PreparedItemStatus status, string code, string? detail = null) => new(status,
        Reason: "EZAPI_PREPARED_" + code + ": creation not attempted" + (detail is null ? "." : "; " + detail + "."));
}
