> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Night shop supply

Use `nightShop: "repeatable"` or `"unique"` in an item JSON declaration, or `NativeItemOptions.NightShop` and optional `IsNightShopAvailable` for a C# factory. Require EZapi0.1.65 or newer. None is the default. Unique means currently not owned, including nested player items; it is not a lifetime purchase limit.

The official Early/Late event argument is an **owned-item snapshot**, not a stock queue. EZapi never modifies that snapshot. Following the official LearnerMod example, the scoped Late callback calls `PlayerStore.AddDirectSellingItemToTable`. Only a complete unowned unit-count increase read back from the actual sale inventory increments `ItemApi.StockSnapshot().Added`. Earlier snapshot-only implementations did not establish real stock placement.

Native placement retains game perk restrictions, ownership and inventory admission. Rejected goods may be destroyed by the native method. EZapi does not touch or destroy an instance after handoff. A native exception records a failure without automatically retrying an uncertain transfer. Added is a confirmed stock-placement count, not a purchase count.

The exact `PlaceInventorInventory(false)` scope and the registered Late delegate are required. Visiting scopes, unscoped events and reentrant events are inert. Each ID is attempted once per synchronous scope; existing unowned sale stock prevents later duplication. Owned items are refreshed before each author predicate. A run, framework generation or sale-table change stops delivery. Bounds:4096sale entries,16384owned entries. Predicates should be read-only and must not change scenes, save or create items.

Public C#/JSON contracts remain compatible. The0.1.65 fixture also executed full native stock generation, BuyItem, current-day SaveGame, disk readback and a new-process load. Two items cost17each; cash525→491and stable item IDs/counts/ownership were restored. Capacity rejection was retained and supply retried only in a later independent scope after temporary vanilla goods were removed. Existing complex machine state stayed unchanged; no sleep or third nightly processing was requested. Specific NPC dialogue selection and third-party mod combinations remain separate acceptance work. See verification/night-placement-native-0.1.65.json for exact scope.
