> 0.1.72-preview.1 packaging: developer tools default off. See [installation](install-and-debug.en.md). The 415-check/save-reload reports below apply to the recorded pre-packaging DLL hash4238A189…; they are historical for the new binary.

# Versioned item-owned JSON (0.1.72 development)

Use `ItemDataApi` for a specific item's upgrade level, batch or author state. It uses the game's public `GetTagReadonly`, `ModifyTag` and `SyncModifiedState` methods. Run progress belongs in run data. The API stages memory only and never requests a game save.

```csharp
public sealed record UpgradeData(int Level, string Note);
var read = ItemDataApi.Read<UpgradeData>(item, "author.example", "upgrade", 1);
if (read.Status is ItemDataStatus.Missing or ItemDataStatus.Present)
{
    var result = ItemDataApi.Stage(item, read.Revision!, new UpgradeData(2, "Level two"), 1);
    // StagedInMemory confirms readback, not disk persistence.
}
```

Run on the initialized game main thread with a live item in the current loaded run. A revision binds the item identity, parent inventory, stack count, native state, load epoch and exact previous JSON. Re-read after movement, stacking, loading or a scene/context change. Do not persist revisions or retain native items across unrelated callbacks. Revision comparison is a current-value comparison, not a complete history log.

`ownerId` is a lowercase Mod ID (max160 characters); `key` contains1–120 ASCII letters/digits or `_-.`. The tag address is `EZAPI_ITEM_DATA_V1|<ownerId>|<key>`. Namespaces prevent accidental conflicts; they are not a security sandbox.

The envelope is `{"version":1,"data":{...}}`. Versions are positive integers; data must not be null. Default System.Text.Json behavior applies, including case-sensitive property names and supported public properties/constructors. Keep serialization getters and deserialization callbacks free of game mutations.

Read results distinguish `Missing`, `Present`, `VersionMismatch`, `Invalid`, `Disabled`, `Unavailable`, and `Conflict`. Missing reads create no tags. Version mismatches preserve raw JSON/version and provide a revision for an explicit author migration. Decode only versions you understand; preserve unknown future data. Invalid/type-mismatched data has no write revision.

Writes can return `Frozen` or `Conflict` before commit. `StagedInMemory` confirms the official write and readback. `Indeterminate` means the native operation may have changed data or created a partial tag; reconcile by reading before deciding what to do. Never retry payments, consumption or upgrades based only on this result. Data and gameplay effects do not share an automatic transaction.

Limits:16 KiB UTF-8 per envelope, depth16;128 keys and64 KiB including keys per item/owner;4096 total tags inspected. The API preserves other namespaces and never clears the item state. Drag, freeze and context checks repeat after author serialization and before/in the native callback.

Unicode data is preserved. Keep keys and schema fields stable across languages; store semantic values or localization keys and resolve player text with `GameLanguage`/`LocalizedItemText` at display time. Item copy/split/merge follows game tag semantics; author identity rules need their own design.

Native graph codec evidence is distinct from disk persistence. Confirm the correct save slot and read the same data after a fresh game load before claiming persistence. See the [current status/index](item-data.index.json), [compiled example](../src/EZapi.Sample/ItemDataExamples.cs), [full signatures](public-api.index.json) and [Chinese guide](item-data.zh-CN.md).

Five fixture nodes passed 18 explicit native-save checks, 27 new-process reload checks and 37 independent disk-readback checks, preserving two owner namespaces and versions. Stage still never saves; this does not guarantee all gameplay combinations or crash recovery. [Evidence](verification/disk-supplier-native-0.1.72.json).
