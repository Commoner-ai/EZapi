# Native prepared items: filled crates, random modules and a genuine injector

Current version is recorded in release-status.json. Current-DLL native contents, opening/effects and save/reload remain pending. Static signatures and boundary stand-ins do not establish gameplay. See [status](release-status.json), [中文](prepared-items.zh-CN.md), [machine index](prepared-items.index.json) and [consumer](examples/prepared-item-dll/PreparedReward.cs).

`ItemApi.Create(id)` uses the ordinary item directory. Certain vanilla gameplay paths use specialized factories for filled contents, randomized modules or genuine/counterfeit conditions. A real ordering Mod received empty crate templates and needed legacy repair. This API exposes explicit prepared choices without changing ordinary creation or repairing existing items.

```csharp
var result = PreparedItemApi.Create(PreparedItemKind.SecurityCrate);
if (result.Status == PreparedItemStatus.Created)
{
    var detachedCrate = result.Item!;
    // Caller owns this detached object and its subsequent native admission/reward policy.
}
```

| Kind | Actual item ID | Native factory |
| --- | --- | --- |
| SecurityCrate | sec_box | LootCrateSecurity |
| MedicalCrate | med_box | LootCrateMedical |
| EngineeringCrate | eng_box | LootCrateEngineering |
| ServiceCrate | service_box | LootCrateService |
| EvidenceCrate | evidence_box | LootCrateEvidence |
| PerformanceModule | system_module_performance | RandomPerformanceModule |
| EfficiencyModule | system_module_efficiancy | RandomEfficiencyModule |
| QualityModule | system_module_quality | RandomQualityModule |
| OverclockModule | system_module_overclock | RandomOverclockModule |
| EcoModule | system_module_eco | RandomEcoModule |
| FinenessModule | system_module_fineness | RandomFinenessModule |
| GenuineInjector | large_purple_injector | CreateRealGenuineInjector |

Preserve the actual `system_module_efficiancy` spelling. Localized names or spelling corrections are not identifiers. Describe() is a pure managed read-only catalog usable offline/on workers. It creates nothing. Other Mods can still affect native factory behavior through their own patches.

Create(kind, owned:true) requires the initialized game main thread, current loaded run, supported build and available freeze gates. Active drag is refused. It enters exactly one native factory. Filled crates retain native randomized contents; the API checks the nonempty child-item graph and LOOT_CRATE_ID_STRING, then initializes the new object's runtime lock through LockHelper.LoadLockedContainer and reads it back. It never fills/refills/unlocks an existing crate.

Success requires one detached root with the expected ID, no overlap with existing run graph nodes, and requested root ownership readback. GeneralHelper.SetItemOwned is used only when needed; descendants retain native state. Detached unowned children may legitimately share native instance ID0; the API does not rewrite them or reject that fact. NestedItems counts instances excluding the root, not unit totals. Module values, injector effects and child-item legality remain native-factory behavior; no specific roll is guaranteed.

Graph limits:8192 nodes,4096 items,16384 combined parent/child references. The detached graph must have complete reciprocal references, no cycles and no external parents. Invalid/oversized graphs fail without truncated success. Pre-existing observed graph fields must match before/after. This is not a transaction over all game state: arbitrary tags, RNG, money and disk state are excluded.

| Status | Meaning |
| --- | --- |
| Created | Item is nonnull; caller takes responsibility for the detached object |
| Invalid | Invalid enum; factory not entered |
| Unavailable | Thread/run/build/directory/preflight unavailable; factory not entered |
| Conflict | Active drag, reentry or stale preflight; factory not entered |
| Indeterminate | Factory entered but result not confirmed; stop automatic retries |

Once FactoryAttempted=true, a failed/throwing creation or even cleanup may already have consumed randomness or run Mod callbacks. Indeterminate withholds an untrusted Item. It does not mean nothing happened, and must never cause an automatic replacement roll.

Failure cleanup rechecks that the current result is detached, independent and contains no pre-existing/current run nodes before one Destroy attempt. Attached, borrowed, malformed, frozen or stale-run results are preserved and reported. DestroyReturned means only that the native cleanup call returned; it does not verify destruction or RNG rollback. SkippedUnprovenGraph, SkippedStaleRun, Failed and FactoryDidNotReturn describe other boundaries. No manual graph-parent clearing or destructive cleanup of another Mod's graph.

Use [ItemOriginApi](item-origin.en.md) for explicit ordinary/stolen and fixed heat policy. Prepared creation never infers legality from the factory or shop. [Delivery receipt](delivery-receipts.en.md) tagging can apply to a root, but CheckHandoff requires exactly one new item instance: populated crates add child instances and do not satisfy this assumption. Use the new [DeliveryGraphAudit](delivery-graphs.en.md) for populated graphs; the caller still owns native admission and its ledger.

The caller owns native admission or safe detached cleanup. After native admission starts, never read or Destroy the old wrapper. Do not replace [catalog metadata scans](item-catalog.en.md) with prepared creation: browsing should not generate filled crates, consume random rolls or allocate child graphs.

Kinds, IDs, statuses and counts are locale-independent. Native display names follow the game locale; caller text uses GameLanguage.Resolve. Source-linked tests use explicit factory/graph/ownership/lock stand-ins for all routes, borrowed/invalid graphs, load changes, admission callbacks, quotas and cleanup failures. Actual distributions, crate lock/opening, child graph save restoration and mixed Mods remain current-DLL native acceptance work.
