> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Machine nights, saves and cold loads

EZapi 0.1.62 verifies real nightly production, native saves and process restarts for material graphs, filled-item and existing-vessel liquid outputs, ordinary batteries and external power. Existing public recipe declarations remain compatible.

Register factories, recipes and power providers during initialization. Recreate independent windows and delegates in each factory. Let the native loader restore saved quantities, energy, state tags and liquid metadata; do not refill or generate inputs in load/scene callbacks.

`LifecycleContext.Items` is lazily captured once **per subscriber callback**. Later subscribers obtain a fresh graph, including after an earlier capture failed. Within one callback, after deleting items, explicitly recapture with `InventoryApi.CaptureRunItems(context.Store)` before reading items again. `IsCurrent` validates the session, not individual item liveness.

The nightly scheduler stores only candidate identities, then resolves each machine from the live graph immediately before processing. A candidate consumed by an earlier machine is skipped without dereferencing its retired wrapper. Attempt tags still identify slot/run/day and persist in native saves; failures do not automatically retry the same night.

External providers must resolve their current source on every callback. Do not capture a native battery in a static provider closure. A synchronous transaction may borrow it only for that batch. Game save files and external/server state are not one transaction; asynchronous and crash recovery are outside this verification.

The disposable slot534 ran two actual nights and two complete process restarts. External energy ended at86; ordinary energy at90. Both liquid source containers ended at8ml. Existing output contained2ml with total Value16 and QualityBasis6; two item outputs each contained1ml, Value8 and QualityBasis3. Default Reject preserved a three-level material graph. Replayed BeforeNight dispatches produced no duplicate effects. Loaded identities, relationships and resource snapshots matched exactly.

See the [Chinese guide](machine-persistence.zh-CN.md), [machine-readable index](machine-persistence.index.json) and verification report (historical identifier `verification/machine-persistence-native-0.1.62.json`; original artifact not distributed). The official machine factory example is `references/probably-stolen-modding/Modding/LearnerMod/PurpleBloodSynthesizer.cs`; current wrapper signatures and actual game results determine compatibility. No Forge code or DLL is required.
