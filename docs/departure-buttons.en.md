> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Adaptive departure buttons

`DepartureApi` extracts the user's DeepSpace RaidEntry/MapButtonLayout into an independent EZapi service. Register after EZapi initialization on the game thread:

```csharp
DepartureApi.Register("author.raid", "author.raid.entry", "Raid", () => MyRaid.Open());
// On shutdown:
DepartureApi.Unregister("author.raid", "author.raid.entry");
```

`MyRaid` is your own destination controller. See [the compilable example](../src/EZapi.Sample/DepartureExamples.cs) and [full contract](departure-buttons.zh-CN.md). The full `DepartureButton` supports Order, Visible, CanEnter, DisplayName, RequireVisit and ReserveLegacySlots. Read-only predicates are refreshed every0.25 seconds and rechecked before clicking. Use context.Language with inline bilingual text or your CSV catalog for any supported locale.

The independent [consumer project](examples/departure-dll/DepartureMod.csproj) references only the distributed EZapi DLL. Build from the developer-package root with `dotnet build docs/examples/departure-dll/DepartureMod.csproj -c Release -p:GameDir='<game root>'`. Install the resulting caller DLL only when wanted. It logs clicks without entering a destination, saving or consuming visits.

Buttons appear on the native departure destination map, after the game's own tutorial/open/close/leave requirements. This API does not bypass departure rules, consume visits, transfer inventory, enter a destination, close the map or save. Your click callback owns those operations and recovery. Simple registration requires remaining visits; opt out explicitly for menus that do not consume visits.

The adapter clones the current native template, replaces its cloned click event/localizers, measures visible graphics and controls, reserves legacy UpperDistrict slots by default, and allocates stable Order/Id slots without moving other Mods. The original left/middle/right search is followed by a bounded visible-area scan. Existing graphic footprints, screen bounds and clipping masks constrain placement. Entries with no space are hidden. Snapshot and diagnostics show Applied/Visible/Placed/Interactable/Error; Queued registration is not proof of placement.

Close, reload, scene change or native-object replacement destroys only owned clones and revokes stale callbacks. Declarations remain until unregistered or framework shutdown. Unknown game builds disable native attachment. No Harmony or Forge/DeepSpace DLL dependency is added. External Mods can move their controls between refreshes; arbitrary combinations and unusual transformed templates require native testing.

The JSON [task index](departure-buttons.index.json) describes the C# API. JSON content packs cannot execute arbitrary travel callbacks.

Offline: `dotnet run --project work/ezapi-departure-tests/DepartureTests.csproj -c Release`. Source-linked tests cover placement invariants, blocked layouts, ownership, stale scopes, predicates, callback faults and reentry; they do not validate native UI. For native checks use disposable saves and either an explicit caller or developer regression mode, which adds two read-only test buttons. Reopen/reload, switch resolution/language and check both original and injected controls. Test buttons do not consume visits or save.
