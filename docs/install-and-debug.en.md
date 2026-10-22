> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Installation and optional developer tools

Release: **0.1.76-preview.1**, assembly version 0.1.76. This is a prerelease for the tested Probably Stolen Demo 050REV5-L baseline and MelonLoader IL2CPP .NET6. Unknown builds disable sensitive integrations.

| Asset | Audience | Contents |
| --- | --- | --- |
| game.zip | Players | One Mods/EZapi.dll; no sample pack or test plugin |
| developer.zip | Mod authors and AI | Same single framework DLL, optional configuration, validator, guides, indexes and examples |

Close the game, then copy the game package's Mods/EZapi.dll to the game Mods directory. Alternatively run `./Install-EZapi.ps1 -GameDir '<game root>'`. The installer verifies the DLL hash, backs up a previous DLL outside Mods and preserves configuration/content/saves. Keep exactly one framework DLL.

Developer tools are disabled by default. Extract developer.zip, enter developer-tools and run `./Set-EZapiDeveloperTools.ps1 -GameDir '<game root>'`. Add `-Disable` to turn them off. Existing settings are backed up. Restart after changes. Manual configuration lives at `UserData/EZapi/developer-tools.json`:

```json
{"enabled": true, "regressionFixtures": false}
```

Missing/invalid/oversized configuration fails closed. The panel provides diagnostics export, registered IDs, inventory/resource inspection, item valuation, recipe guides and explicit one-item generation. Generation changes the current inventory and a later normal game save retains those changes; use a test save. Reports are written to `UserData/EZapi/diagnostics.json`; pair them with the same session's MelonLoader/Latest.log. Review identifying information before sharing.

Public presets do not enable regression fixtures. Maintainers can explicitly set both options to true in a disposable environment; this registers test content and exposes freeze/night/language/persistence fixtures. The old EZapi.TestTools.dll is a separate internal automation plugin with fixed save/run bindings and save/force-close actions. It is not distributed in these assets. Move any old copy out of Mods while the game is closed; a configuration switch cannot unload another DLL.

Disabling the panel does not delete test items or save data. Follow content authors' migration/removal instructions before uninstalling the framework. Runtime save-block errors remain visible even without the developer panel.

The developer package puts sample packs under examples/ContentPacks; install them into Mods/EZapiPacks only when desired. Avoid simultaneously installing DLL and JSON examples that declare the same IDs. The offline validator needs .NET8. See [AI entry](AI-START-HERE.en.md), [workflows](developer-workflows.index.json) and [public signatures](public-api.index.json).

Runtime 0.1.76 adds [native UI components and AI preview](native-ui-preview.en.md), plus bilingual developer tools. Exact scoped observations and hash are in [release status](release-status.json); full framework, mixed-Mod and save/reload acceptance remain pending. Older DLL evidence applies only to its original build.
