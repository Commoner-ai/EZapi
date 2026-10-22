# EZapi

**Probably Stolen 模组前置框架**：为玩家提供单DLL前置，为开发者和AI提供DLL API、JSON内容包、文档、调用示例及调试工具。框架源码不公开。

An independent **Probably Stolen** Mod framework: DLL APIs, declarative JSON packs, an offline validator, buildable examples and machine-readable AI indexes. Prefer official game APIs. No Forge runtime dependency or bundled game assemblies.

**0.1.73-preview.1** is a prerelease, with assembly version 0.1.73. Reference functionality has 39 implemented groups; this is not a promise of binary compatibility or complete gameplay validation.

**Public distribution repository:** packages, documentation and consumer examples. Framework implementation source and internal Git history are not published here.

[中文说明](README.zh-CN.md) · [Downloads](https://github.com/Commoner-ai/EZapi/releases) · [Install and debug](docs/install-and-debug.en.md) · [AI entry](docs/AI-START-HERE.en.md)

| Package | For | What to install |
| --- | --- | --- |
| game.zip | Players | One Mods/EZapi.dll. Developer UI defaults off; no sample/test plugin |
| developer.zip | Mod authors and AI | Same runtime plus optional settings, validator, guides, indexes and samples. Do not copy tooling DLLs into game Mods |

Install compatible MelonLoader IL2CPP .NET6 first. Close the game and copy Mods/EZapi.dll from the game package, or run `./Install-EZapi.ps1 -GameDir '<game root>'`. Players do not need another EZapi debug plugin. Enable/disable the panel using the script in developer-tools, then restart.

The optional panel exports diagnostics, lists IDs, inspects inventory/resources, estimates value, opens guides and explicitly generates a selected item. Regression fixtures default off. Internal EZapi.TestTools.dll is excluded from public assets. Examples stay under examples until explicitly installed.

The developer package includes [game entry and batch automation](docs/automation.en.md) in `developer-tools/automation/`: entry, guarded input, batch regression and localization scripts. After enabling tools, run `Enter-EZapiTestStore.ps1 -GameDir '<game root>' -ExpectedSlot <test slot>`. Basic entry needs no additional game DLL.

## Develop a Mod

Framework implementation source and build projects are private. The developer package provides DLL/XML, exact public API indexes, guides, JSON schemas, a binary offline validator, consumer examples and automation scripts. See [adaptive departure buttons](docs/departure-buttons.en.md).

Build the independent example against your installed EZapi and MelonLoader:

```powershell
dotnet build docs/examples/minimal-dll/SimpleMod.csproj -c Release -p:GameDir='<game root>'
```

Language follows game settings. Additional CSV locales remain supported with English/author fallback; Chinese/English are priority acceptance locales. Official docs: [probably-stolen-modding](https://github.com/dragonpan2/probably-stolen-modding). Framework build and fetch scripts are internal.

The departure API creates a new DLL hash. Earlier native language/save evidence remains tied to its historical hash. Offline verification is not native gameplay verification. See [release notes](docs/releases/0.1.73-preview.1.md).

Framework implementation is private. See LICENSE for binary redistribution and consumer-example permissions, and THIRD-PARTY-NOTICES.md for dependencies.
