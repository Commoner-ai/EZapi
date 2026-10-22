> 0.1.72-preview.1 packaging: developer tools default off. See [installation](install-and-debug.en.md). The 415-check/save-reload reports below apply to the recorded pre-packaging DLL hash4238A189…; they are historical for the new binary.

# Current game language and Chinese/English text

## Current language requirements

This milestone prioritizes Chinese (`zh`) and English (`en`) translations and verification, without restricting the framework to those languages. Player-facing EZapi text follows the current game language setting, without a separate EZapi language selector. Do not select text from the Windows language or the language used when a save was created. Resolve text again on the next refresh or reopening after a game language change; existing and reloaded items are also in the acceptance scope. Samples and milestone text provide Chinese and English; the framework does not translate literal strings automatically.

Automatic CSV resolution supports all official locale codes: use an available translation for the current game locale, then English, then readable author fallback text. Complete translations and native verification for other languages are outside this milestone's delivery requirements.

The current DLL passed 415 native en/zh checks, including existing and disk-loaded items, tooltips, container/machine titles, reading, workshop, dialogue, phone and supplier pages. Supplier arrival/departure callbacks were fixture-dispatched; natural visits, payment and delivery were not tested in this run. See [scoped save/supplier evidence](verification/disk-supplier-native-0.1.72.json).

DLL Mods can read the current language through `GameLanguage.Current`, then resolve bilingual text or a content-pack CSV catalog through `GameLanguage.Resolve`. EZapi reads the native locale getter for the verified game build. Since EZapi 0.1.72, ordinary JSON item display fields can bind directly to keys in their own pack's CSV files; every referenced key must have an English entry before the pack is queued.

```csharp
var language = GameLanguage.Current;
if (language.IsFallback)
    log.Warn(language.Reason ?? "Game locale unavailable; using English fallback.");

// Resolve one page with one snapshot. Read Current again the next time it opens or renders.
var title = language.Resolve(new LocalizedItemText("维修顾问", "Repair adviser"));
var body = language.Resolve(new LocalizedItemText("需要了解什么？", "What would you like to know?"));
```

Read `Current` only on the initialized game main thread. An unknown game build, an uninitialized runtime, another thread, a missing/invalid locale, or a native getter failure returns `LocaleCode="en"`, `IsFallback=true`, and a diagnostic `Reason`. It does not change the game language or retain a Unity `Locale` object.

`GameLanguageSnapshot` is a managed snapshot of one observation and does not refresh itself. Resolve text when opening/rendering a UI, getting a phone contact name, or running a dynamic dialogue callback. Capture one snapshot for one page so all labels use the same locale. Do not cache resolved strings in static initialization.

`LocalizedItemText` selects Chinese for `zh*` and English for other locale codes; if the selected side is absent, it falls back to the other side. Use CSV for additional languages:

```csharp
var catalog = Localization.GetPack("author.example");
if (catalog is not null) {
    var text = GameLanguage.Resolve(catalog, "repair_help", "Help unavailable", 3);
    Render(text.Text); // requested locale → en → readable caller fallback
    if (text.MissingKey || text.FormatError is not null)
        log.Warn($"repair_help: {text.FormatError ?? "translation missing"}");
}
```

CSV lookup uses the exact current game locale, such as `en`, `zh`, `fr`, or `ptBR`. `Localization.Resolve(locale,...)` remains a pure managed explicit-locale entry point for offline tools and contexts without a native game locale. Use `GameLanguage.Resolve` when following the player's current setting. A missing-key fallback must be readable player text, never an internal ID.

| Text surface | API |
| --- | --- |
| Native item name, short description, flavor and source | `NativeItemOptions.LocalizedName`, `LocalizedShortDescription`, `LocalizedFlavorText`, `LocalizedSourceName` |
| Recipe guide | `Localized*` properties on `RecipeGuideEntry` / `RecipeGuideFigure` |
| Upgrade/service requirement hint | `RequirementDefinition.Message` |
| Dynamic dialogue, phone, supplier or custom UI text | `GameLanguage.Resolve(LocalizedItemText)` or `GameLanguage.Current.Resolve(...)` |
| More than Chinese/English | `LocalizationCatalog` with `GameLanguage.Resolve(catalog, key, fallback, args)` |
| Ordinary JSON item name, descriptions, flavor and custom text | `localizedName`, `localizedDescription` / `localizedShortDescription`, `localizedLongDescription`, `localizedFlavorText`, `localizedCustomText` |
| Type, module prose, source, tooltip and container title | `typeNames[].localizedName`, `module.localizedAdditionalEffectDisplay`, `tooltip.localizedSourceName` / `labels[].localizedText` / `lines[].localizedText`, `container.localizedTitle` |
| DLL `NativeItemOptions` and similar typed declarations | Inline `LocalizedItemText(chinese, english)`; resolve dynamic dialogue/phone/custom UI text when displayed |
| Multi-language JSON content-pack resources | `Localization/<locale>.csv`, checked atomically by `--pack`/`--packs`; a missing English key rejects the pack |
| Standalone JSON machine window and slot labels | `localizedTitle`, `liquidInputs[].localizedLabel`, `output.localizedLabel`, `localizedBatteryLabel`, `localizedManualLabel`; see the [machine localization guide](machine-layout-localization.en.md) |

Each JSON `localized*` object can contain inline `chinese` and `english` values, or a `key` from the owning pack. Inline text may accompany a CSV key as the last readable fallback: requested locale → `en` → inline text → the literal field. For example:

```json
{
  "id": "author.example.storage_box",
  "modId": "author.example",
  "name": "Storage box",
  "localizedName": { "key": "storage_name" },
  "container": {
    "width": 4,
    "height": 3,
    "title": "Box contents",
    "localizedTitle": { "key": "storage_title" }
  },
  "tooltip": {
    "lines": [{ "text": "4×3 capacity", "localizedText": { "key": "storage_capacity" } }]
  }
}
```

`Localization/en.csv` must define `storage_name`, `storage_title` and `storage_capacity`. Other supported locale tables may add translations; missing translations use English. Item CSV strings reject format placeholders because these fixed fields do not bind runtime parameters. Applied items refresh on display-name reads and when their storage window is reopened. EZapi only refreshes its own registered items and framework-written names; player-renamed items and foreign items are preserved. Offline evidence covers the field routing and refresh logic, not actual game UI, Harmony behavior, language switching or save/reload.

The game's built-in ModLoader guide uses `ModHelper.GetCurrentLocale().Identifier.Code` and `ModHelper.GetLocalized`. EZapi keeps its MelonLoader DLL API independent and does not imitate ModHelper or promise loader ABI compatibility. It adapts the locale getter for the exact current game build and retains an English fallback. See [localization.zh-CN.md](localization.zh-CN.md) for the official headerless CSV format and escaping rules.

Compiled examples: [LanguageExamples.cs](../src/EZapi.Sample/LanguageExamples.cs) and the [bilingual JSON pack](../src/EZapi.Sample/Content/items.json). The pack parser, CSV bindings and item refresh have offline evidence. Current-DLL native en/zh switching, fixture items/tooltips/reopened storage and CSV display are included in the 415 checks cited above. Five fixture items passed 18 native save-stage, 27 new-process reload and 37 independent disk-readback checks.

## 0.1.72 presentation APIs

Dialogue/phone/supplier graphs accept Localized* properties. Resolve dynamic callback replies using context.LocaleCode. ReadingApi.Open accepts bilingual title/body/signature. WorkshopSnapshot.ForGameLanguage(...) resolves optional Localized* fields and built-in buttons on open/refresh; legacy English flags retain their fixed-language behavior. New Mods should use ForGameLanguage to follow the game setting. See dialogue-localization.index.json for all fields and lifecycle boundaries. Reading, workshop, dialogue, phone and fixture-dispatched supplier pages are included in the scoped evidence above.
