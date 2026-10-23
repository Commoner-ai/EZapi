# Item delivery receipts and reconciliation

Current version is recorded in release-status.json. Current-DLL native delivery, overnight and save/reload acceptance remain pending. Offline tests do not establish native behavior. See [release status](release-status.json), [中文](delivery-receipts.zh-CN.md), [machine index](delivery-receipts.index.json) and the [buildable consumer](examples/delivery-receipt-dll/ReceiptDelivery.cs).

Gifts, orders and one-time rewards can share stable operation IDs, item-owned receipts and fresh-inventory reconciliation. EZapi supplies tagging and observation. The Mod owns its item factory, destination, native admission, payment, ledger and save policy.

```csharp
var receipt = new DeliveryReceipt(savedOperationId, "YOUR_ITEM_ID", Units: 1);
var scan = ItemReceiptApi.Capture("author.mod", "delivery-receipts");
var result = DeliveryReceiptAudit.Reconcile(scan, receipt, savedLedgerState);
```

Generate an operation ID once before delivery, for example `Guid.NewGuid().ToString("N")`, and persist it with the caller ledger. Reuse it after reload, full inventory or errors. Generating a new ID on each click defeats duplicate prevention. Owner is a lowercase Mod ID; the dedicated item-data key accepts 1..120 ASCII letters/digits/dots/hyphens/underscores. `ItemDataApi` isolates the owner's tag namespace. Do not mix other JSON formats under this key.

| Caller ledger / observation | Decision | Caller behavior |
| --- | --- | --- |
| Pending and complete scan finds no token | CanAttempt | Preparation only; Pending must mean native delivery was never attempted |
| Pending or InFlight; exactly one token, correct item/count, player-owned and in an inventory | Observed | Compare-stage Delivered; retain InFlight if the ledger update is uncertain |
| InFlight and no current token | Indeterminate | Stop automatic reissue and reconcile through business/manual handling |
| Duplicate token, wrong item/count, detached or non-player-owned match | Conflict | Stop; never sum duplicate quantities to hide conflicts |
| Delivered, including sold/consumed items or unavailable scan | Recorded | Caller completed ledger takes precedence; never award again; no claim about current inventory validity |
| Failed scan or malformed input | Indeterminate / Invalid | Never interpret as an empty successful scan |

Ledger states are caller assertions, not EZapi-maintained records. Use [RunDataApi](run-data-records.zh-CN.md) for run/slot isolation and compare-stage updates. Stage only changes memory. Item tags and ledger updates are not a disk transaction. A persistent-completion claim requires actual game save completion and a cold-load readback.

Delivery sequence:

1. Read the ledger. Delivered exits. InFlight only reconciles; it does not create another item.
2. Only Pending creates an owned detached item. Set exact units, origin and other data before `ItemReceiptApi.StampDetached`. Continue only when Present/StagedInMemory supplies a Stamp. Different, damaged or old-version tags are preserved and refused.
3. Preflight one empty whole-item slot without merging. Capture the run before admission. Compare-stage and read back InFlight, then recheck run, inventory and admission immediately before handing over.
4. Set `attempted=true` before exactly one native call. `SlotMarker.TryAcceptOnce` and `GraphUtils.EmporiumTryAdd` have different paths; verify semantics for the actual game build. Do not manually detach graph parents or Expel first.
5. From handoff onward, never read, destroy, roll back or reuse the original item wrapper, even after a native exception/rejection. Native code may already have destroyed it. Use only fresh Capture results and the managed Stamp in CheckHandoff.
6. Observed from CheckHandoff means that exact item entered this load's captured run and pre-existing observed fields stayed unchanged. Compare-stage the ledger next. Other outcomes stay unresolved. Unchanged inventory alone never resets InFlight to Pending.

The consumer demonstrates one whole-item empty-slot TryAcceptOnce. After `NativeHandoffAttempted=true`, caller cleanup must never touch the original wrapper. False also does not authorize unconditional cleanup: verify it still belongs to the caller and no callback delivered it. The example does not destroy items, retry, save or pay. Native return/exception diagnostics do not override fresh inventory evidence.

CheckHandoff compares operation, native/instance identity, item ID, units, ownership and parent inventory within the same load/Epoch. It requires exactly one new item and unchanged prior records. Optional requiredInventoryIdentity enforces an exact destination; omission allows another player inventory. Never serialize native identities or turn them back into pointers.

Reconcile supports recovery after reload using a durable operation ID and expected contents, without requiring old pointers/Epoch. It observes current items, not delivery history. Copied tags can impersonate receipts: this is cooperative duplicate prevention, not authentication or anti-cheat.

Snapshots omit geometry, locks, arbitrary item tags, container-content correctness, money and disk state. Empty containers can be checked as a whole. A reward with nested child items adds multiple records and conservatively fails CheckHandoff's single-addition rule. Splits, merges, partial delivery and batches need caller-designed ledgers and independent operation IDs.

StampDetached/Capture require the initialized main thread, current loaded run and supported build. Tag writes also require available freeze gates, an unfrozen item and no drag. Capture makes two fresh bounded scans and compares them: max4096 items and 1MiB aggregate receipt JSON characters per pass. Bad tags, limits or failures return no partial evidence. Do not scan on every UI frame.

DeliveryReceiptAudit is pure managed and can run offline/on workers with stable complete input collections. IDs, enums, units and error codes are locale-independent. Resolve player text with GameLanguage.Resolve; localized names are never identity keys.

Source-linked tests use explicit native stand-ins for duplicate/wrong items, reloads, missing evidence, reentry, quotas, uncertain tags and invalidated old wrappers. Native admission, mixed Mods, overnight saving and cold-load recovery remain pending for this DLL.

For populated containers use [DeliveryGraphAudit](delivery-graphs.en.md); retain the single-item audit contract.
