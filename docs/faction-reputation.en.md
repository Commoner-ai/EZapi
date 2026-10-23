# Faction reputation and extensions

Since 0.1.78, the native paper window has a searchable, vertically scrolling faction list and a right-side reputation ruler with fixed hover details. There is no bottom benefit list or scrollbar. Players install one `Mods/EZapi.dll`; developer mode is optional.

## Native discovery

Every 0.25 seconds while open, EZapi reads current `PlayerStore.storeReputations` and each faction's actual perks. Names, effects, flavor, signed thresholds and active flags come from the native objects. The native `PerkElement.Init` supplies sprites; hovering uses `ReputationUIManager.UpdateToolTipContent` and its resulting text, including ordinary Mod postfix additions. Native paper, font, information and close behavior remain. Both header hit targets are 44×44 logical pixels.

Native faction order is retained; registered providers follow in Order/ID order. Effects use descending thresholds with stable ties; ruler markers use actual negative-to-positive thresholds, not five representative moods. Equal or nearby thresholds share a counted marker without dropping effects. Hover displays the first entry; marker clicks or footer arrows cycle effects, and a small tick points to the selected effect's exact threshold. Details persist after leaving the marker and clear on faction selection. Very long text uses native TMP pages with page arrows; it never adds a bottom scrollbar or grows the window.

The native range starts at -200..200 and expands for native Mod thresholds/current values. Provider definitions specify their own range; include every threshold, otherwise markers clamp to endpoints. Native reads are bounded to 512 factions/256 perks each; provider registrations to 256 factions/256 perks each.

## One provider registration

See the [buildable consumer](examples/faction-dll/Factions.cs), [JSON example](examples/faction-ui/free-traders.json) and [schema](../schemas/faction-ui.schema.json).

```csharp
FactionUiApi.Register("author.guild", new FactionUiDefinition(
    "author.guild.free_traders", new("自由商会", "Free traders")) {
    Color = "#71546D", Minimum = -200, Maximum = 200
}, context => {
    var data = RunData.Open("author.guild"); // Resolve the current run each read.
    return new FactionReputationView(data.GetDouble("reputation", 0)) {
        Perks = new[] { new FactionPerkView("author.guild.discount",
            new("商会优惠", "Guild discount"), new("商会商人给予优惠。", "Guild traders offer a discount."),
            60, data.GetBool("discount-active")) { Icon = "star" } }
    };
});
```

`Queued` accepts the presentation declaration. `Open()` opens the native reputation window, `Snapshot()` returns immutable views, and `Unregister(ownerId, factionId)` only removes that presentation. Active/AlwaysActive are authoritative provider flags. The framework never changes reputation, unlocks effects, pays or saves. The owner still implements faction gameplay and persistence.

Callbacks are main-thread, read-only and may run four times per second. Return null to hide an undiscovered faction. Do not register/unregister, mutate gameplay or save inside them. Stale-run results are discarded; one faulty provider cannot disable others. Do not cache native objects across loads.

`FactionUiJson.Parse(stream)` strictly validates one appearance definition; `RegisterJson(ownerId, stream, readState)` registers it with a live C# provider. Allowed fields: id/name/color/order/minimum/maximum, with Chinese/English names and at least one readable side. Maximum 16 KiB; unknown/duplicate fields are rejected. This is not an automatically loaded item content pack.

## Lifecycle and compatibility

The exact `ezapi.presentation.faction-browser` hook group binds synchronously after native OpenUI initialization. Closing stops refresh, then restores native controls and frees owned objects/callbacks. Scene changes and reloads clear stale references. Native entries win ID collisions. Fully replacing the native window with a private Canvas requires explicit integration; automatic discovery does not make a display-only provider a native saved faction.

The optional panel's extension probe registers 24 transient factions with 28 effects each through this same API, including shared thresholds and long paged text. Closing clears them without native faction/save writes. Read [release status](release-status.json) for exact-DLL observations and pending acceptance.
