# Item origin, stolen status and heat

Use `ItemOriginApi` for native stolen status on an actual delivery/reward item. It calls `StolenHelper` and verifies readback. Check [release status](release-status.json) for current-DLL acceptance.

| Task | Call | Meaning |
| --- | --- | --- |
| Read origin | `Read(item)` | Native IsStolen and nullable FoundChancePercent |
| Fixed 100% stolen heat | `StageStolen(item)` | Also corrects already-stolen factory results |
| Fixed 0..100% heat | `StageStolen(item, heatPercent)` | **Zero means fixed zero, never random** |
| Ordinary item | `StageClean(item)` | Native removal; already ordinary is Unchanged |
| Native initialization | `InitializeNative(item)` | InitStolenItem(item,0); preserves already-stolen heat |

```csharp
var result = ItemOriginApi.StageStolen(item, heatPercent: 100);
if (result.Status is ItemOriginStatus.StagedInMemory or ItemOriginStatus.Unchanged)
{
    // result.After is confirmed memory state. Continue the Mod's delivery workflow.
}
else
{
    // Stop subsequent delivery/payment. Indeterminate requires Read/reconciliation.
    Log(result.Reason);
}
```

Buildable consumer: [Origin.cs](examples/item-origin-dll/Origin.cs) and [project](examples/item-origin-dll/Origin.csproj). From the SDK root use `dotnet build docs/examples/item-origin-dll/Origin.csproj -c Release '-p:GameDir=<your game directory>'`. Native GameItem parameters require your own generated game wrappers; those assemblies are never redistributed. Set `-p:EZapiDll=<local candidate>` to override the SDK DLL.

Heat is an **integer percentage**, not a 0..1 fraction. Read uses native IsStolenItem/GetFoundChance, never the random IsFound detection roll. Ordinary items return null chance; unavailable reads never impersonate ordinary items. Out-of-range third-party values are rejected without clamping or overwriting them.

`InitializeNative` invokes native initialization once on an ordinary item. Low-value native rules may leave it ordinary: success returns actual state, **not a guaranteed stolen item**. Existing stolen items return Unchanged without rerolling heat. Fixed-zero StageStolen uses positive native initialization followed by SetHeat(0), avoiding native/random zero semantics. Native helpers retain their own tags; the framework does not handcraft tag names or replace detection/nightly rules.

Writes require the initialized game main thread and current run, a ready inventory-freeze gate, no drag/freeze, and either player-owned inventory items or detached items under the caller's control. Do not pass shared directory templates or another owner's detached item: a bare wrapper cannot prove provenance. Run/load epoch, instance, ID, stack count, parent and state/dictionary identity are checked between native operations. Recursive writes are rejected. Reacquire GameItem after reload; no item wrappers are retained by this API.

Operations affect the entire native object; they neither split stacks nor multiply heat by count. StageClean follows RemoveStolenStatus; it is not a paid laundering service and does not clear contraband categories or custom Mod provenance. Success synchronizes modified state and verifies origin, **without requesting a save**. Native/disk persistence for this DLL remains pending acceptance.

Read/Unchanged perform no writes. StagedInMemory means readback succeeded. Preflight rejections have NativeWriteAttempted=false. Once a native write starts, exceptions, changed contexts or mismatches return Indeterminate, true attempted, and no unconfirmed After snapshot. Never blindly retry, restore arbitrary native fields, charge/deliver, or destroy an item already placed in an inventory. Read helps reconcile current values; it cannot reconstruct random-sequence or other-Mod side effects.

States/numbers are locale-independent. Native tooltips retain native localization; use GameLanguage.Resolve for your own Chinese/English messages. Reason codes and English diagnostics are author-facing. There is no new JSON item field: this is an explicit live-instance action; contraband category and stolen origin are distinct.

Source-linked offline tests exercise boundaries, native stand-ins, freezes, ownership, reentry and partial failures. They do not establish native gameplay, overnight heat changes, detection events or save/reload. See the [machine index](item-origin.index.json) and [error index](errors.index.json).
