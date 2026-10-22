> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Machine readiness ()

Call `MachineRegistrationApi.Snapshot()` on the initialized game main thread. Views capture managed state at query time; query again after state changes.

| Field | Meaning |
| --- | --- |
| Status | Native item catalog application state; does not guarantee resources |
| RuntimeInstalled | Global machine runtime and nightly lifecycle readiness, independent of this definition's EnableNightly flag |
| ProcessingAvailable | Applied catalog, editable recipe runtime, inventory admission and freeze protection available; not a promise of successful processing |
| NightlyInstalled | RuntimeInstalled and EnableNightly=true; does not guarantee tonight's production |

A machine with nightly processing disabled can report runtime=true, manual=true, nightly=false. Manual processing does not require nightly lifecycle readiness. Recipe editing is unavailable during processing, so ProcessingAvailable is false then.

The original ten-argument constructor and ten-field deconstruction remain available. RuntimeInstalled is an additive init property; legacy manually constructed views default to false. Use Snapshot for actual readiness.

Evidence: 168 offline registration checks; 128 native registration checks and 867 checks across seven native suites; 19 checks reading the previous version's persistent fixture save. Full public behavior coverage remains under review.

See [Chinese guide](machine-status.zh-CN.md), [AI index](machine-status.index.json), and verification (historical identifier `verification/machine-status-native-0.1.63.json`; original artifact not distributed).
