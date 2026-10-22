> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Read-only nightly batch quota callbacks ()

`MachineDefinition.ResolveNightlyBatchCount` returns the number of batches to attempt. It must not change machines, resources, inventories or saves. The default is1 and the valid range is1..64. Use `EnableNightly=false` to disable automatic processing; returning0 is invalid.

The resolver runs before each batch. A smaller quota stops early; later increases cannot exceed the first quota. The first failed batch stops the loop. Manual `ProcessBatch` attempts one batch and never invokes this resolver.

Before invoking the resolver, EZapi records the complete machine item graph, including a plain root without contents. It checks identities, counts, ownership, geometry, parents, state/dictionary identities, all tags, base values, windows, graph edges and inventory locks. Existing energy/liquid checks remain, and inventories are resolved again after the callback. A difference stops production with `nightly count resolver changed resources`.

Limits are4096 items,8192 nodes/edges,16384 total tags and4096 tags per item. Invalid or excessive graphs reject before the callback. References live only for the synchronous observation; this is not an asynchronous transaction or crash recovery mechanism.

The per-night attempt marker is written before author code and persists with the game's normal save. Repeated events and scheduler restarts do not grant extra attempts. EZapi does not save automatically. The guard detects changes and prevents production; it does not undo arbitrary illegal author writes. Use recipe transactions for material changes. Never destroy objects, load a save, change scenes or write inventories in the resolver.

Validation:49 offline suites; automation263/0 and graph journal47/0 link production source with explicit native/session/graph stubs. Temporary native objects cover12 scenarios/36 checks; registration164/0 and seven native groups1102/0. This proves the observation boundary, not a new real night event. The existing machine passed19 read-only checks, remains at2 commits, and all536 ES3 files are byte-identical. Two rejected fixture setup attempts are retained separately. No third night was requested.

Coverage remains37/39 groups. Full registration/runtime behavior auditing continues, including ordinary recipe callback tag auditing. This release does not claim that every machine callback is fully audited.
