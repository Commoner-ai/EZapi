> Current developer guide. See [release status](current-status.en.md). This update changes documentation and consumer examples; the runtime DLL is unchanged. Offline validation/compilation does not establish native visual acceptance.

# Optional native-style item tooltips

Choose a presentation recipe before implementing an item. Native style follows Probably Stolen's own hierarchy. Extended style can expose Mod attribution and additional state. These recipes use existing fields; **there is no new `tooltipStyle` JSON property**, and existing Mods are not forced to change.

| Choice | Native style | Extended information |
| --- | --- | --- |
| Description placement | `descriptionPlacement: "native"` | Usually also `native`; use official early/late additions when needed |
| Mod attribution | Explicit `tooltip.showSource: false` | Optional `true` with a localized source name |
| Extra lines/labels | Omit by default; show necessary unique state once | Use `tooltip.lines`, `tooltip.labels` or module text callbacks |
| Instructions and narrative | Instructions in `shortDescription`, narrative in `flavorText` | Keep the same distinction and avoid repetition |
| Categories/value/properties | Let the game render real item state | Preserve native output as well |

The released `ShowSource` default is **true**. Omitting `tooltip` does not disable attribution. This documentation does not change that default for existing consumers.

The SDK generator exposes the choice directly (scripts live in `distribution/` in source, `developer-tools/` in the SDK):

```powershell
./developer-tools/New-EZapiMod.ps1 -Kind Json -ModId author.example -OutputDir ./MyNewMod -TooltipStyle Native
```

Use `-Kind Dll` for an independent C# project or `-TooltipStyle Extended` for attribution. The generator defaults to Native and creates a new directory only; it never installs, delivers items or saves. Its default material category is an example to adjust by actual gameplay.

## Native hierarchy

The supplied vanilla juice screenshot and the current Tooltip path show the item name, cyan/teal bracketed categories, estimated/base value, applicable properties, then separated muted gray-green italic narrative. Specialized categories add their own content. Calories, levels and instructions are conditional, not required on every item.

| Element | Source |
| --- | --- |
| Name, once | `name` / `localizedName` |
| Actual categories | `types`, with `typeNames` for custom categories; the game supplies brackets, color and native translations |
| Value | `value` and the game's valuation logic, not a manually written duplicate price |
| Applicable properties | Real `food`, `module` or other definitions/official helpers, not prose pretending to implement behavior |
| Optional instructions | `shortDescription` / `localizedShortDescription`; only information not already expressed |
| Optional narrative | `flavorText` / `localizedFlavorText`; native rendering supplies spacing, color and italics |

The game controls fonts, borders, wrapping, width and specialized ordering. Do not hard-code HTML, font sizes or colors, or replace the entire Tooltip window to copy its appearance. Choose types by gameplay semantics, not an item's brand or image. See the [property index](item-properties.index.json) for native and custom categories.

`longDescription` is not guaranteed to appear in ordinary hover tooltips. `customText` in native mode is an item text field that can change inventory-icon presentation, not a generic Tooltip paragraph. Keep essential instructions in the appropriate visible field.

## Applying this to the processor screenshot

The source line is extended attribution. The repeated processor model after “Level 1” adds duplication. For native style, disable attribution and choose the actual gameplay category. Keep a level once if it affects gameplay and is not already rendered; a plain material needs no invented level. Put narrative in flavor text and preserve essential instructions as instructions.

The [complete JSON pack](examples/tooltip-native/pack.json) and [items](examples/tooltip-native/items.json) assume a plain material solely to demonstrate this recipe; they do not define the existing Mod's gameplay, artwork or delivery. Key settings:

```json
"descriptionPlacement": "native",
"tooltip": {"showSource": false}
```

Validate from the SDK root:

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --pack docs/examples/tooltip-native
```

The [independent DLL project](examples/tooltip-dll/TooltipMod.csproj) and [Mod.cs](examples/tooltip-dll/Mod.cs) show `ItemDefinition` registration with `DescriptionPlacement = "native"` and `Tooltip = new() { ShowSource = false }`. Build it with `GameDir` and, when needed, `EZapiDll` as in the [minimal caller guide](AI-START-HERE.en.md).

For `ItemApi.RegisterItem/RegisterModule` factories, use `new NativeItemOptions(ShowSource: false, ...)` and provide native descriptions/flavor text through the factory or matching Options. `NativeItemOptions` has no `DescriptionPlacement` parameter.

## Late does not mean the final line

The official `OnCreateTooltipEarly/Late` hooks append content. In the current game build, native short descriptions, flavor text and some specialized content are still rendered **after Late**. Extended attribution, labels and late lines may therefore appear above those paragraphs. Do not promise final-line attribution.

Prefer native placement with attribution disabled for the vanilla look. Early/late placement clears EZapi's declared native description fields and appends their text during that event, with different positioning/spacing. Do not clear a shared builder, call its `Build`, or globally replace Tooltip generation to move attribution. Limit additions to owned items and preserve other Mods. See [Presentation API](presentation.zh-CN.md).

## Copyable AI instructions

> Use EZapi's optional native Tooltip recipe. Read docs/tooltip-style.en.md and tooltip-style.index.json, then use actual schema/API fields: descriptionPlacement="native", tooltip.showSource=false; for declarations use ItemDefinition.Tooltip.ShowSource=false, for factories NativeItemOptions(ShowSource:false). Show the name once. Choose types by real gameplay and let the game draw categories, value and helper-provided properties. Never repeat those values in prose. Put necessary instructions in shortDescription and narrative in flavorText; omit empty sections. Do not substitute longDescription/customText, hand-write HTML/colors/category/price lines, or replace the window. Show unique dynamic state once when needed. Follow the game language with separate Chinese/English descriptors or CSV; never translate stable IDs. Distinguish offline checks from in-game visual acceptance.

## Verification and references

Validate the JSON pack and compile the caller against the exact framework DLL. In game, compare against the same vanilla category: hierarchy, attribution removal, no repeated names/stats, clear instructions, Chinese/English, narrow windows, changing state and reloaded items. These new examples have no current in-game visual or save/reload acceptance.

Based on the [official ModdingGuide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md), supplied screenshots, the framework's native field mapping and read-only inspection of the current game's rendering path. Colors and specialized order remain game-version dependent. See the separate [art guide](item-art.en.md) and [machine-readable recipe index](tooltip-style.index.json).
