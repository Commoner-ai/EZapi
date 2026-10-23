# Complete native and Mod item catalog

Use this for stock pools, encyclopedias and queries such as “all items with native base value above 150”. [Release status](release-status.json) separates exact-DLL observations from offline checks.

| Need | API | Runs factories? |
| --- | --- | --- |
| EZapi declarations/application states | `ItemApi.Snapshot()` / `FactorySnapshot()` | No |
| Actual vanilla and other Mod item IDs | `ItemCatalogApi.CaptureIdentifiers()` | No |
| Native names, base values and sprite paths | `BeginScan()`, then `Step()` across frames | One temporary instance per ID |
| An explicit subset | `BeginScan(ids)` | Only when Step is called |

## Start here

Use the initialized **game main thread with a loaded run**. Call BeginScan once, then one Step per Update. Default Step attempts at most four factories and checks a 4ms budget between factory calls. A synchronous native factory cannot be preempted, so one call may exceed that budget. Never loop to completion inside one frame or OnGUI.

```csharp
using var scan = ItemCatalogApi.BeginScan(); // keep this handle across Updates in a real Mod
// In each Update: var progress = scan.Step();
// Only when progress.Status == ItemCatalogStatus.Complete:
// var stock = scan.Snapshot().Where(x => x.BaseValue > 150).ToArray();
// Inspect scan.Failures before accepting the pool.
```

Copy the [compilable consumer](examples/item-catalog-dll/Catalog.cs) and [project](examples/item-catalog-dll/Catalog.csproj) for an actual multi-frame lifecycle. Dispose on scene changes. There is no background worker or hidden polling.

From the extracted developer-package root, run `dotnet build docs/examples/item-catalog-dll/Catalog.csproj -c Release`. It defaults to the bundled `Mods/EZapi.dll` and needs no game assemblies. In a source checkout or copied project, pass `-p:EZapiDll=<absolute DLL path>`; `-p:GameDir=<game root>` selects the installed DLL. This helper is not an automatically loaded Mod: wire Begin/Tick/Dispose into your own Mod lifecycle.

## Contract and compatibility

`CaptureIdentifiers()` copies the current official `DirectoryMaster.GetIdentifierList<GameItem>()`: vanilla, EZapi and other native-directory Mods. IDs are deduplicated and sorted ordinally. Above 65,536 raw directory entries, capture fails explicitly; it never returns the first N as a complete catalog. Capture is point-in-time; begin again after dynamic catalog changes.

BeginScan executes no factories. Step calls `ItemApi.Create(id, owned:false)`, reads managed metadata and destroys the detached instance before returning. It never delivers, charges or requests saving. Third-party factory code still executes: factories must return fresh detached items and must not grant rewards, save, change scenes or return existing inventory instances. The framework cannot undo arbitrary factory side effects.

Step accepts 1..64 items and a finite positive time budget up to 50ms. Complete means every captured ID has been attempted, **including failed factories**. Snapshot includes successful entries, Failures names rejected IDs and exception types. Pending/cancelled/invalidated/cleanup-failed scans cannot expose a partial pool as complete. A cleanup failure terminates the entire scan.

DisplayName uses native GetDisplayName in the captured game locale. Run identity includes slot, run and load epoch; same-slot reload and locale changes invalidate the handle. Rebuild both scan and consuming UI after switching to English. A previously extracted managed snapshot does not translate or update itself.

A changed locale code does not prove its tables are ready. Begin reuses the asynchronous readiness gate used by native UI previews and rejects with `EZAPI_CATALOG_LOCALE` during loading; wait and begin again. This API never switches language or forces synchronous WaitForCompletion.

BaseValue is native GetBaseValue as a long integer credit amount, **not the final sale price**. SpriteAtlasPath/SpritePath are native keys only: there is no pixel readback, black/transparent-art detection or GPU guarantee. Consumers may resolve the original sprite with native RenderHandler.LoadFromAtlasRaw on the main thread and separately validate/cache their own artwork policy. Nonempty paths do not prove valid art. The scan retains no native GameItem/Sprite/inventory references between Steps.

The directory is not a list of ready-to-sell finished products. `BaseValue > 150` is only a query example. Crates, reward packages and quest items may require additional native generation, filling or locking; scanning does not perform those gameplay steps. A Mod reproducing thief or supplier stock must use that source's actual eligibility and generation rules.

## Diagnostics

The optional developer panel provides “Export all native/Mod items”. Only clicking starts a batched scan. Output is `UserData/EZapi/item-catalog.json`; `debugUi.itemCatalog` is the diagnostic summary. Use a test save because native factories execute; no save is requested.

`EZAPI_CATALOG_NOT_READY/BUILD/LOCALE` means unavailable native prerequisites; `ID/LIMIT/BUDGET` reports invalid inputs; `REENTRY` rejects operations inside a factory; `STALE` requires a new scan; `FACTORY` affects one entry; `CLEANUP` stops the scan. See [task index](item-catalog.index.json) and [error index](errors.index.json). Offline boundary tests do not establish arbitrary-Mod or native gameplay acceptance.
