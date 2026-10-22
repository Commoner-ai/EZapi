# EZapi 0.1.61: bilingual recipe guides

Recipe guides reuse `LocalizedItemText(Chinese, English)`. Existing constructors and fixed text remain supported. Add optional properties to provide translations:

```csharp
var figure = new RecipeGuideFigure("author.tools.material", 2) {
    LocalizedName = new("材料", "Material"),
    LocalizedRequirement = new("必须属于玩家", "Must be owned"),
    LocalizedQuantity = new("2份", "2 units")
};
```

`RecipeGuideEntry` adds `LocalizedMachineName` and `LocalizedNote`. `RecipeGuideDisplay` adds the same three properties as a figure, and `RecipeGuideOptions` adds `LocalizedNote`. Automatic machine guides preserve these values and the machine's existing `NativeItemOptions.LocalizedName`.

The browser reads the game's official current locale on the main thread. Chinese locales (`zh*`, ignoring case) select Chinese; all other locales select English. A missing translation falls back to the other language. Omitted descriptors preserve the original fixed string. The open guide refreshes after a language change without registering content again or rewriting saved item names.

The browser's buttons, headings, pagination and missing-image labels also switch languages. Framework-generated explanations for stack quantities, tags, container slots and dynamic outputs provide both languages. Authors must supply translations for their own text; EZapi does not machine-translate it.

`RecipeGuideRules.ResolveLocale(entry, locale)` produces a managed display view for tools. It preserves stable IDs, counts and the source catalog. The browser caches this view until the selection, catalog revision or language changes.

JSON uses camelCase: `"localizedName": { "chinese": "材料", "english": "Material" }`. At least one nonblank language is required. Limits per language are 200 characters for names/quantities, 1000 for requirements and 4000 for notes. Explicit null values, unknown fields, duplicate keys, invalid controls and oversized text are rejected.

Use the [bilingual JSON example](examples/recipe-guide-bilingual.json), [machine-readable field index](recipe-guide-bilingual.index.json) and `MachineGuideExamples.RegisterBilingualGuide`. The CLI accepts `--recipe-guide author.tools <file>` with the same rules.

Game verification covers the current supported build. The dedicated disposable-slot check temporarily selects actual English and Chinese Unity locales, verifies rendered guide text, closes the guide and restores the original language. It does not request a game save or prove that every text string in a third-party Mod has translations.
