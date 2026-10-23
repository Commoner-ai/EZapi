# Filled-container delivery graph audit

Use [release-status.json](release-status.json) for current version and acceptance. Current-DLL native admission, overnight and save/reload are pending. [中文](delivery-graphs.zh-CN.md) · [machine index](delivery-graphs.index.json) · [buildable consumer](examples/delivery-graph-dll/GraphDelivery.cs).

A populated crate adds several item instances, exceeding the single-addition contract of [DeliveryReceiptAudit.CheckHandoff](delivery-receipts.en.md). `ItemGraphReceiptApi` captures managed graph observations; pure `DeliveryGraphAudit` compares root, descendants and inventory edges. It performs no admission, payment, ledger update or save.

```csharp
// Persist this stable OperationId before delivery; reuse it after exceptions and reload.
var tagged = ItemReceiptApi.StampDetached(crate, "author.mod", "receipts", receipt);
if (tagged.Stamp is null) return;
var detached = ItemGraphReceiptApi.CaptureDetached(crate, "author.mod", "receipts");
var before = ItemGraphReceiptApi.CaptureRun("author.mod", "receipts");
// Caller checks one empty whole-item slot, stages/reads back InFlight and rechecks both graphs.
// Exactly one native admission; never access the old root, descendants or marker afterward.
var after = ItemGraphReceiptApi.CaptureRun("author.mod", "receipts");
var audit = DeliveryGraphAudit.CheckHandoff(before, after, detached, targetIdentity);
```

This fragment shows observation order. The [complete example](examples/delivery-graph-dll/GraphDelivery.cs) includes admission and ledger gates; retain them in any real reward flow.

## Attempt and recovery

1. Read the caller ledger for the exact slot/run. Delivered stops; InFlight only reconciles. Pending must mean native admission was never attempted.
2. Use a [prepared factory](prepared-items.en.md) or verified native route. Finish content changes, then `StampDetached` the owned detached root. `CaptureDetached` requires an unparented root, closed descendant graph and unique matching root receipt. It does not fill, repair or change descendants.
3. `CaptureRun` includes main/save-bag inventories, including an empty target. Check an empty whole-item slot without merging. Compare-stage/read back InFlight, then recheck run, admission and both graphs.
4. Attempt native admission exactly once. From that moment, even on native failure/exception, never read/destroy/reuse the old root, descendants or marker. Take a fresh `CaptureRun` and use `CheckHandoff`.
5. Only Observed confirms delivery within these observed fields; compare-stage Delivered next. Preserve uncertainty on any other result; no automatic reissue, cleanup, Pending reset or save request.

`CheckHandoff` requires identical slot/run/Epoch and namespace across all three observations. The detached graph cannot borrow existing nodes. Exactly that graph must be added, existing nodes/items must stay unchanged, and only the existing target inventory gains the root edge. The root must enter exactly that target. Omitting the target permits any inventory observed before admission; zero is invalid. Internal edges, native identity, item ID, units, instance ID, ownership, parent inventory and addressed receipts are compared.

Unowned native children may share instance ID0. Default policy preserves child ownership and instance IDs. Explicit `allowChildOwnershipInitialization: true` permits only unowned-to-owned children and assignment of their instance IDs, while retaining unique owned IDs, pointers, item IDs, units, parents, receipts and topology. It does not permit root ownership changes or content changes.

`Reconcile(snapshot, receipt, ledgerState)` follows single-receipt decisions: Delivered→Recorded; complete Pending absence→CanAttempt; InFlight absence→Indeterminate; duplicate/mismatched token→Conflict; one correct owned inventory root→Observed. **Reload reconciliation confirms the current root receipt, not historical original child contents.** Persist and check a separate expected content manifest if cold-load child verification is needed. Never serialize native pointers or Epoch as cross-load identity.

## Scope, cost and language

Native captures require the initialized game main thread, current loaded run and supported build. Two complete scans must agree. Mutation, bad tags, exceptions or limits return no partial evidence. Limits:8192 nodes,4096 items,16384 combined parent/child references and1 MiB receipt JSON characters per pass. Do not scan every UI frame.

Managed analysis supports offline/workers; inputs must be complete and immutable during analysis. Numeric node identities are only same-Epoch comparison keys, never handles to resolve. Snapshots exclude geometry, locks, other tags, quality/heat, money, arbitrary game state and disk persistence. Cooperative snapshots/receipts are not authenticated; other Mods can copy or change them. Splits, merges, partial transfers, batches or native topologies outside this contract remain unconfirmed.

IDs, codes and enums are locale-independent; use `GameLanguage.Resolve` for player text. Offline cases cover populated graphs, mutation/duplicates/missing children, ownership initialization, load changes, limits, reentry and invalid old wrappers. They do not establish native admission, graph persistence or mixed-Mod acceptance.
