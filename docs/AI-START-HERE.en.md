> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# EZapi developer and AI entry point

Current prerelease: **0.1.74-preview.2**, assembly 0.1.74. [Public distribution](https://github.com/Commoner-ai/EZapi). Framework implementation, offline tests and build projects are MIT open source; see [source builds](build-from-source.en.md). Use the public DLL APIs, metadata, guides and consumer examples.

For artwork matching the original game, use the optional [item art guide](item-art.en.md), [asset/workflow index](item-art.index.json) and bundled same-category references.

## Choose a task

- [Workflows](developer-workflows.index.json): JSON packs or DLL APIs, example and failure boundary.
- [Optional native-style tooltips](tooltip-style.en.md): native categories/value/properties, separate instructions/narrative, explicit attribution opt-out, extended-style alternative, JSON/DLL examples and [machine index](tooltip-style.index.json).
- [Exact public signatures](public-api.index.json): types, overloads, parameters, defaults and XML documentation from the current binary; no method bodies.
- [Task index](API.index.zh-CN.json), [domain index](source.index.json), [errors](errors.index.json). The repository domain index links implementation sources; the binary SDK lists modules and links to GitHub.
- [Adaptive departure buttons](departure-buttons.en.md), [installation](install-and-debug.en.md), [entry automation](automation.en.md), [中文入口](AI-START-HERE.zh-CN.md).

## Download the developer package

Use developer.zip from [Releases](https://github.com/Commoner-ai/EZapi/releases). It contains the same runtime DLL as game.zip, XML comments, binary validator, schemas, guides, consumer examples and automation tools. Samples are opt-in and developer tools default off. Players install one Mods/EZapi.dll. The GitHub source ZIP at the current release tag includes framework and tool sources; ready-to-use binaries are in release assets.

JSON packs go under `Mods/EZapiPacks/<packId>/` with pack.json and items.json. See [pack guide](content-packs.zh-CN.md), [manifest schema](../schemas/pack.schema.json), [item schema](../schemas/items.schema.json) and [sample](../src/EZapi.Sample/Content/items.json). From the extracted developer package:

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --pack Mods/EZapiPacks/author.example
dotnet tools/ezapi-validate/ezapi-validate.dll --packs Mods/EZapiPacks
dotnet tools/ezapi-validate/ezapi-validate.dll --help
```

Validation does not launch the game, deliver items or prove native application/save restoration.

Offline starter/search tools are included in the SDK (`distribution/` in source):

```powershell
./developer-tools/New-EZapiMod.ps1 -Kind Json -ModId author.example -OutputDir ./MyNewMod -TooltipStyle Native
./developer-tools/Find-EZapiApi.ps1 -Query 'ItemRegistry.Register'
```

Choose `-Kind Dll` or `-TooltipStyle Extended` when needed. Generation requires a fresh directory, creates bilingual text following game settings, and uses a material category to adjust by gameplay. Search returns actual overload/task/error metadata without loading game assemblies.

## Build a consumer DLL

Copy [the minimal project](examples/minimal-dll/SimpleMod.csproj) and [Mod.cs](examples/minimal-dll/Mod.cs), change the author namespace and stable IDs, install EZapi/MelonLoader, then run:

```powershell
dotnet build ./SimpleMod.csproj -c Release '-p:GameDir=<your game directory>'
```

Install only the generated caller DLL into Mods. Do not copy additional framework/loader DLLs. The [standalone departure example](examples/departure-dll/DepartureMod.csproj) builds from the developer-package root against the bundled DLL.

## Respect language and lifetimes

Language follows game settings. Use GameLanguage.Current with inline bilingual text or CSV catalogs, including additional supplied locales and fallback. See [language contract](game-language.en.md). Accepted/Queued is registration acceptance, not native Applied state. Keep predicates read-only, reacquire identities after reload/scene changes, and do not blindly retry ambiguous payments, deliveries or saves. Destination travel and persistent transactions belong to the calling Mod.

## Verification scope

59 offline suites and 25 CLI process checks passed, including 141 source-linked departure/browser checks. 669 direct game references matched and static reachable Unity wrappers had no unstripping stubs. Scoped native checks on this DLL cover existing Mod entries, 84 additional temporary cards, a new category, vertical scrolling, search and window cleanup. **Full framework native acceptance and save/reload remain pending.** Historical language/save reports apply only to their recorded DLLs. Package manifests record hashes and boundaries.

Prefer the [official Probably Stolen guide](https://github.com/dragonpan2/probably-stolen-modding), the actual game signatures and each API result. EZapi has no Forge DLL dependency and promises no Forge binary compatibility.
