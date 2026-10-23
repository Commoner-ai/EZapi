# Current EZapi version and verification

Version 0.1.87 removes the whole generated wrapper DLL SHA gate from language reads and existing-item localization refresh. Main-thread, exact getter signature, locale value and refresh-hook checks remain; failed reads are retried at the next presentation. Public API signatures remain unchanged and the 0.1.86 empty-description fix is retained. This change has offline regression evidence; actual locale switching, mixed-Mod use and loaded-item presentation still require current-DLL native acceptance.

Current prerelease **0.1.87-preview.1**, assembly **0.1.87**, MIT. Distributed through GitHub Releases. Players install one `Mods/EZapi.dll`; the SDK includes that same DLL, XML, validator, schemas, guides, consumer examples and entry automation. Debug defaults off.

Optional developer tools add “+10,000 credits”: one native grant per completed click with balance readback, no automatic grant/save request. Normal saving retains the change; see [developer tools](install-and-debug.en.md).

The retained [faction presentation](faction-reputation.en.md) adds live faction discovery, search, real threshold markers, native sprites and native hover effects. The fixed bottom detail panel has no benefit list scrollbar. Counted markers cycle dense/shared effects; long text uses pages. Native header controls have larger hit targets. The provider API owns presentation only; gameplay/persistence remain with the Mod. The 0.1.77 Wild opening fix is retained.

The build entry runs 67 offline suites, 25 CLI process checks and independent consumers. Read [release status](release-status.json) for exact hashes, executed results and scoped native observations. The 39 implemented reference groups describe coverage, not full gameplay acceptance.

**Full framework gameplay, arbitrary Mod combinations and current-DLL save/reload remain pending.** Older 0.1.74/0.1.76/0.1.77/0.1.78/0.1.79 evidence remains historical and never transfers. Private logs/saves are not distributed.

Start with the [AI guide](AI-START-HERE.en.md), [exact APIs](public-api.index.json), [tasks](developer-workflows.index.json), [source build](build-from-source.en.md), [automation](automation.en.md) and [native UI](native-ui-preview.en.md). [History](history/README.md) records its own binaries. Original artwork retains separate rights and is excluded from MIT.


[Complete native/Mod item catalog](item-catalog.en.md): all IDs, explicit incremental metadata reads, per-item failures and run/locale invalidation.

[Item origin and heat](item-origin.en.md): ordinary/stolen items, fixed percentages and native initialization with guarded readback.

[Artwork inspection](artwork-inspection.en.md): source sprite crops, dark/transparent content, tight meshes, GPU and pure RGBA analysis.

[Delivery receipts](delivery-receipts.en.md): stable gift/order tokens, detached item tags, fresh handoff audits and reload reconciliation. Uncertain delivery never triggers automatic reissue.

[Native prepared items](prepared-items.en.md): explicit filled crates, random modules and a genuine injector; ordinary templates remain available and failures never automatically reroll.

[Filled-container graph audit](delivery-graphs.en.md): compare root, descendants and inventory edges; uncertainty preserves the caller ledger without automatic reissue.
