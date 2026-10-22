> 0.1.72-preview.1 packaging: developer tools default off. See [installation](install-and-debug.en.md). The 415-check/save-reload reports below apply to the recorded pre-packaging DLL hash4238A189…; they are historical for the new binary.

# Bilingual dialogue, phone calls and suppliers

The 0.1.72 development build adds optional `Localized*` init properties. Create a reusable script during initialization; EZapi reads the game locale once when a native dialogue graph opens. An active graph retains its opening locale. Reopen the original script, or continue a supplier menu, to use the next locale. See the [field/task index](dialogue-localization.index.json), [full signatures](public-api.index.json), and compiled [dialogue](../src/EZapi.Sample/DialogueExamples.cs) / [supplier](../src/EZapi.Sample/SupplierExamples.cs) examples.

`zh*` selects Chinese; all other locales select English. A missing side uses the supplied side. `DialogueDefinition` covers speaker, body, confirmation, back, leave, later, failure and pagination. `DialogueChoice` covers the option, preview and per-choice confirmation. Supplied localized text must be readable and nonblank; labels are bounded to 512 UTF-16 characters, body/preview/failure to 131072. Only newline, carriage return and tab control characters are accepted. A corresponding literal may be empty when a localized override is supplied, but never null.

Built-in default Chinese button/failure values resolve to English in an English game. Custom literals are preserved. Legacy constructors cannot distinguish an omitted default from explicitly supplied identical text: a literal exactly equal to the built-in Chinese default is treated as that default. To pin it, explicitly set both sides to the same value, such as `LocalizedConfirmLabel = new("确认", "确认")`.

Dynamic replies should use the graph snapshot carried by `DialogueContext.LocaleCode`:

```csharp
context => new LocalizedItemText("欢迎！", "Welcome!").ForLocale(context.LocaleCode)!
```

For CSV, use `catalog.Resolve(context.LocaleCode, key, readableFallback, args).Text`. These DLL declarations have no owning content-pack catalog, so implicit `LocalizedItemText.Key` bindings are rejected. Resolve the catalog explicitly. Avoid permanently resolving a reusable script during initialization.

Phone calls share the same graph behavior. Contact names remain `PhoneContact.DisplayName` callbacks; use `GameLanguage.Resolve` there. The existing refresh/open path queries the callback again.

`SupplierDefinition` covers speaker, confirmation, back, continue, leave, failure and pagination; `SupplierPage.LocalizedBody` covers captured menu text; `SupplierOffer` covers option, preview and per-offer confirmation. Capture callbacks receive capture-time locale, while purchase callbacks receive the opened graph locale. A visit may arrive in a different locale from the one used when queued. Continue captures a new menu and opens a new graph. Purchase exceptions are diagnosed once through `EZAPI_SUPPLIER_PURCHASE`, with the failure text resolved in the opening locale.

Localization preserves stable IDs, run/epoch checks, at-most-once confirmation, automatic-offer rules and owned-resource cleanup. It does not translate author prose, make payments, deliver items or save. Linked production-source tests cover the managed/native-adapter paths. Current-DLL English/Chinese pages, native choice labels and cleanup are included in the [415 scoped native checks](verification/disk-supplier-native-0.1.72.json). Supplier arrival/departure callbacks were fixture-dispatched; this run did not test natural arrival, payment or delivery.

## Reading and workshop entry points

ReadingApi.Open accepts three LocalizedItemText values for title, body and signature; all three resolve using one locale read before native note allocation. Workshop providers can return WorkshopSnapshot.ForGameLanguage(...); its Localized* fields and built-in buttons follow the current locale on open/refresh. Legacy constructors retain their explicit English flag unless FollowGameLanguage is enabled. A visible snapshot stays stable until refreshed. These declarations do not infer a content-pack CSV catalog; resolve CSV explicitly in author callbacks.
