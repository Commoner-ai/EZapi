# EZapi 0.1.69 machine contract review

The machine review covers **34 types, 114 properties and 20 methods**. Each entry in `machine-public-contract.audit.json` states EZapi behavior, source and bounded evidence. This is independent functional coverage, not Forge binary compatibility. Output kinds are the strings `items` and `container`; processing returns `MachineBatchResult`.

Use `MachineRegistrationApi.RegisterMachine` before native directory application for a new machine. Use `RegisterAdditionalRecipes` on an idle main thread to append a coherent batch while retaining each contributor and order. `MachineRuntimeApi.ProcessBatch` processes one synchronous batch. A template-bound processor can borrow actual child inventories from an existing custom window; the window author still installs native battery/module/manual interactions.

Native references in inventories, contexts and plans are borrowed for the current synchronous call. Resolve them again after scene/load/window changes. Stop invalidates generation-bound factories, processors and callback contexts. Dispose subscription and condition leases; each lease releases its own contribution.

Night processing defaults to one batch. A readonly quota resolver accepts 1..64, runs again after each successful batch, and cannot increase the initial cap. Persistent slot/run/day/instance attempt markers prevent another attempt in the same night, including after a failed author callback. No automatic save, asynchronous processing, networking or crash-recovery transaction is promised.

Readonly callback guards detect illegal writes and refuse commit; they do not undo arbitrary author mutation. The standard transaction separately journals material graphs, liquid composition, energy and output delivery. Custom transactions own their resource semantics. `RecoveryFailed` requires manual inspection; a postcommit notification error does not refund or retry a committed batch.

The unchanged installed 0.1.69 binary passed **1557 native checks in 11 groups**, plus **19 old-machine checks**. The original machine still has two commits. All **536 ES3 files remained byte-identical after game exit**. Earlier actual two-night saves, full-process reloads and English/Chinese guide rendering are reused with explicit source continuity; this batch requested no night/save.

The wider inventory of all149 reference types exposed remaining public helpers: per-mod logging, pointer capture state and terminal ID normalization. See `public-contract-coverage.index.json`. Their presence outside the39 primary API groups still counts toward the user's coverage requirement, so complete coverage remains pending.
