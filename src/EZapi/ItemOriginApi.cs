using Il2Cpp;

namespace EZapi;

/// <summary>Origin results describe memory only. Indeterminate requires reconciliation before any retry.</summary>
public enum ItemOriginStatus { Read, Unchanged, StagedInMemory, Invalid, Unavailable, Frozen, Conflict, Indeterminate }

/// <summary>Managed native-origin snapshot. FoundChancePercent is null for ordinary items, including after cleaning.</summary>
public sealed record ItemOriginSnapshot(bool IsStolen, int? FoundChancePercent);

/// <summary>Read result; an unavailable snapshot is never represented as an ordinary item.</summary>
public sealed record ItemOriginReadResult(ItemOriginStatus Status, ItemOriginSnapshot? Snapshot = null, string? Reason = null);

/// <summary>Native write/readback result. A successful stage does not deliver, pay for, or save the item.</summary>
public sealed record ItemOriginWriteResult(ItemOriginStatus Status, ItemOriginSnapshot? Before = null,
    ItemOriginSnapshot? After = null, bool NativeWriteAttempted = false, string? Reason = null);

/// <summary>
/// Native stolen status and integer percentage heat for live items. Main thread and loaded run required.
/// Writes accept caller-owned detached items or player-owned inventory items, respecting freezes and drag.
/// No native tags are recreated by hand, no item wrappers are retained and no save is requested.
/// </summary>
public static class ItemOriginApi
{
    private static readonly HashSet<IntPtr> Writing = new();

    /// <summary>Read native IsStolenItem and, only for stolen items, GetFoundChance. No random detection roll.</summary>
    public static ItemOriginReadResult Read(GameItem item)
    {
        if (!GameSession.OnThread) return new(ItemOriginStatus.Unavailable, Reason: "EZAPI_ORIGIN_THREAD: use the initialized game main thread.");
        try
        {
            var identity = Capture(item);
            return new(ItemOriginStatus.Read, Snapshot(item, identity));
        }
        catch (Exception error) { return new(ItemOriginStatus.Unavailable, Reason: Failure("UNAVAILABLE", error)); }
    }

    /// <summary>Remove stolen status through the native helper; already ordinary items remain untouched.</summary>
    public static ItemOriginWriteResult StageClean(GameItem item) => Stage(item, Mode.Clean, 0);

    /// <summary>
    /// Set stolen status and an exact integer 0..100 percent heat (default 100).
    /// Zero means a stolen item at 0 percent, never native/random initialization.
    /// Existing stolen factory results are also updated and verified.
    /// </summary>
    public static ItemOriginWriteResult StageStolen(GameItem item, int heatPercent = 100) => Stage(item, Mode.Fixed, heatPercent);

    /// <summary>
    /// Call native InitStolenItem(item,0) once for an ordinary item; preserve an already-stolen item's heat.
    /// Native low-value rules may leave the item ordinary. Success returns the actual result, not guaranteed stolen status.
    /// Never blindly retry: initialization may consume the native random sequence.
    /// </summary>
    public static ItemOriginWriteResult InitializeNative(GameItem item) => Stage(item, Mode.Native, 0);

    private enum Mode { Clean, Fixed, Native }
    private sealed record Identity(GameRunIdentity Run, IntPtr Pointer, int Instance, string Id, int Units,
        IntPtr Parent, IntPtr State, IntPtr Dictionary);

    private static Identity Capture(GameItem item)
    {
        var run = NativeAccess.RequireRun();
        if (!GameBuildFingerprint.IsKnown) throw new InvalidOperationException("unsupported game build");
        NativeAccess.RequireItem(item);
        if (string.IsNullOrWhiteSpace(item.identifier) || item.state is null || item.state.Pointer == IntPtr.Zero ||
            item.state.dict is null || item.state.dict.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("live item state required");
        var identity = new Identity(run, item.Pointer, item.uniqueId, item.identifier, item.unitCount,
            item.parentInventory?.Pointer ?? IntPtr.Zero, item.state.Pointer, item.state.dict.Pointer);
        RequireCurrent(item, identity);
        return identity;
    }

    private static void RequireCurrent(GameItem item, Identity identity)
    {
        // Check run before touching the old wrapper: the same slot/run can be reloaded.
        if (!NativeAccess.SameRun(identity.Run) || item.Pointer != identity.Pointer ||
            item.uniqueId != identity.Instance || item.identifier != identity.Id || item.unitCount != identity.Units ||
            (item.parentInventory?.Pointer ?? IntPtr.Zero) != identity.Parent || item.state?.Pointer != identity.State ||
            item.state?.dict?.Pointer != identity.Dictionary)
            throw new InvalidOperationException("item or run changed");
    }

    private static ItemOriginSnapshot Snapshot(GameItem item, Identity identity)
    {
        RequireCurrent(item, identity);
        var stolen = StolenHelper.IsStolenItem(item);
        RequireCurrent(item, identity);
        int? chance = stolen ? StolenHelper.GetFoundChance(item) : null;
        RequireCurrent(item, identity);
        if (StolenHelper.IsStolenItem(item) != stolen)
            throw new InvalidOperationException("origin changed during read");
        RequireCurrent(item, identity);
        // Never clamp a third-party out-of-range value and present it as valid.
        if (chance is < 0 or > 100) throw new InvalidOperationException("native heat outside 0..100 percent");
        return new(stolen, chance);
    }

    private static ItemOriginStatus? Gate(GameItem item, Identity identity)
    {
        RequireCurrent(item, identity);
        if (!InventoryFreezeApi.IsAvailable) return ItemOriginStatus.Unavailable;
        var frozen = InventoryFreezeApi.IsFrozen(item);
        RequireCurrent(item, identity);
        if (frozen) return ItemOriginStatus.Frozen;
        if (InventoryApi.IsItemDragActive) return ItemOriginStatus.Conflict;
        if (identity.Parent != IntPtr.Zero)
        {
            var owned = GeneralHelper.IsItemOwned(item);
            RequireCurrent(item, identity);
            if (!owned) return ItemOriginStatus.Conflict;
        }
        RequireCurrent(item, identity);
        return null;
    }

    private static ItemOriginWriteResult Stage(GameItem item, Mode mode, int heat)
    {
        if (!GameSession.OnThread) return new(ItemOriginStatus.Unavailable, Reason: "EZAPI_ORIGIN_THREAD: use the initialized game main thread.");
        if (mode == Mode.Fixed && (heat < 0 || heat > 100))
            return new(ItemOriginStatus.Invalid, Reason: "EZAPI_ORIGIN_HEAT: integer percent in 0..100 required.");
        var pointer = IntPtr.Zero;
        var locked = false;
        var attempted = false;
        ItemOriginSnapshot? before = null;
        try
        {
            // Acquire the lock before calling native predicates: they can be patched by other Mods.
            NativeAccess.RequireRun();
            if (item is null || item.Pointer == IntPtr.Zero)
                return new(ItemOriginStatus.Invalid, Reason: "EZAPI_ORIGIN_ITEM: live item required.");
            pointer = item.Pointer;
            if (!(locked = Writing.Add(pointer)))
                return new(ItemOriginStatus.Conflict, Reason: "EZAPI_ORIGIN_BUSY: recursive origin write refused.");
            var identity = Capture(item);
            if (Gate(item, identity) is { } blocked) return Blocked(blocked);
            before = Snapshot(item, identity);
            if (Gate(item, identity) is { } blockedAfterRead) return Blocked(blockedAfterRead);
            // A patched predicate can change origin even when the operation would
            // otherwise be a no-op. Never return an old snapshot as Unchanged.
            if (Snapshot(item, identity) != before) return Blocked(ItemOriginStatus.Conflict);
            if (mode == Mode.Clean && !before.IsStolen || mode == Mode.Native && before.IsStolen ||
                mode == Mode.Fixed && before.IsStolen && before.FoundChancePercent == heat)
                return new(ItemOriginStatus.Unchanged, before, before);
            if (Gate(item, identity) is { } blockedBeforeWrite) return Blocked(blockedBeforeWrite);
            attempted = true;
            if (mode == Mode.Clean) StolenHelper.RemoveStolenStatus(item);
            else if (mode == Mode.Native) StolenHelper.InitStolenItem(item, 0);
            else
            {
                // Init(...,0) has native/random semantics, so bootstrap fixed zero
                // with positive heat, then set zero explicitly. Preserve native tag setup.
                if (!before.IsStolen) StolenHelper.InitStolenItem(item, heat == 0 ? 100 : heat);
                if (Gate(item, identity) is not null) return Unconfirmed();
                var initialized = Snapshot(item, identity);
                if (!initialized.IsStolen || initialized.FoundChancePercent != heat)
                {
                    if (Gate(item, identity) is not null) return Unconfirmed();
                    StolenHelper.SetHeat(item, heat);
                }
            }
            if (Gate(item, identity) is not null) return Unconfirmed();
            item.SyncModifiedState();
            if (Gate(item, identity) is not null) return Unconfirmed();
            var after = Snapshot(item, identity);
            if (mode == Mode.Clean && after.IsStolen || mode == Mode.Fixed && (!after.IsStolen || after.FoundChancePercent != heat))
                return Unconfirmed();
            return new(ItemOriginStatus.StagedInMemory, before, after, true);
        }
        catch (Exception error)
        {
            return attempted ? Unconfirmed(error) : new(ItemOriginStatus.Unavailable, before, Reason: Failure("UNAVAILABLE", error));
        }
        finally { if (locked) Writing.Remove(pointer); }

        ItemOriginWriteResult Blocked(ItemOriginStatus status) => new(status, before,
            Reason: "EZAPI_ORIGIN_GATE: wait for freeze/drag to finish and use a player-owned inventory item or a caller-owned detached item.");
        ItemOriginWriteResult Unconfirmed(Exception? error = null) => new(ItemOriginStatus.Indeterminate, before,
            NativeWriteAttempted: true, Reason: error is null ? "EZAPI_ORIGIN_UNCONFIRMED: native write attempted; read and reconcile before retrying."
                : Failure("UNCONFIRMED", error));
    }

    private static string Failure(string code, Exception error) => "EZAPI_ORIGIN_" + code + ": " + error.GetType().Name;
}
