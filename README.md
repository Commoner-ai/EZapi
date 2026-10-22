# EZapi

[Current release and verification](docs/current-status.en.md) · [Historical records / 历史资料](docs/history/README.md)

**Probably Stolen 模组前置框架**：为玩家提供单DLL前置，为开发者和AI提供DLL API、JSON内容包、文档、调用示例及调试工具。框架、校验器与测试采用 MIT 开源。

An independent **Probably Stolen** Mod framework: DLL APIs, declarative JSON packs, an offline validator, buildable examples and machine-readable AI indexes. Prefer official game APIs. No Forge runtime dependency or bundled game assemblies.

**0.1.74-preview.2** is a prerelease, with assembly version 0.1.74. Reference functionality has 39 implemented groups; this is not a promise of binary compatibility or complete gameplay validation.

**MIT open source:** framework implementation, offline validator, tests, build tooling, documentation and examples are available here. Older private history is not imported. See [build from source](docs/build-from-source.en.md).

[中文说明](README.zh-CN.md) · [Downloads](https://github.com/Commoner-ai/EZapi/releases) · [Install and debug](docs/install-and-debug.en.md) · [AI entry](docs/AI-START-HERE.en.md)

| Package | For | What to install |
| --- | --- | --- |
| game.zip | Players | One Mods/EZapi.dll. Developer UI defaults off; no sample/test plugin |
| developer.zip | Mod authors and AI | Same runtime plus optional settings, validator, guides, indexes and samples. Do not copy tooling DLLs into game Mods |

Install compatible MelonLoader IL2CPP .NET6 first. Close the game and copy Mods/EZapi.dll from the game package, or run `./Install-EZapi.ps1 -GameDir '<game root>'`. Players do not need another EZapi debug plugin. Enable/disable the panel using the script in developer-tools, then restart.

The optional panel exports diagnostics, lists IDs, inspects inventory/resources, estimates value, opens guides and explicitly generates a selected item. Regression fixtures default off. Internal EZapi.TestTools.dll is excluded from public assets. Examples stay under examples until explicitly installed.

The developer package includes [game entry and batch automation](docs/automation.en.md) in `developer-tools/automation/`: entry, guarded input, batch regression and localization scripts. After enabling tools, run `Enter-EZapiTestStore.ps1 -GameDir '<game root>' -ExpectedSlot <test slot>`. Basic entry needs no additional game DLL.

Optional [original-style item art guide](docs/item-art.en.md): the developer package includes 96 original reference sprites, eight boards, transparent enlargements and AI prompts. Original artwork retains separate rights.

Choose the optional [native or extended Tooltip recipe](docs/tooltip-style.en.md). The SDK includes a bilingual JSON/DLL starter with `-TooltipStyle Native|Extended` and offline API search; see the [AI entry point](docs/AI-START-HERE.en.md).

## Develop a Mod

Framework implementation and build projects are available under MIT; see [source build instructions](docs/build-from-source.en.md). The developer package provides DLL/XML, exact public API indexes, guides, JSON schemas, a binary offline validator, consumer examples and automation scripts. See [adaptive departure buttons](docs/departure-buttons.en.md).

Build the independent example against your installed EZapi and MelonLoader:

```powershell
dotnet build docs/examples/minimal-dll/SimpleMod.csproj -c Release -p:GameDir='<game root>'
```

Language follows game settings. Additional CSV locales remain supported with English/author fallback; Chinese/English are priority acceptance locales. Official docs: [probably-stolen-modding](https://github.com/dragonpan2/probably-stolen-modding). Build and reference-fetch scripts are available in this repository.

This SDK revision adds Tooltip documentation, consumer examples and offline developer tools while keeping the preview.1 runtime unchanged. That runtime provides the Wild network browser with native cards, category tabs, search and a permanent vertical scrollbar. Earlier native language/save evidence remains tied to its historical hash. Offline verification is not native gameplay verification. See [release notes](docs/releases/0.1.74-preview.2.md).

See the MIT LICENSE for source and binary reuse, and THIRD-PARTY-NOTICES.md for dependencies. Game/runtime DLLs and private workspace data are not redistributed.

Original game artwork references retain separate rights and are excluded from MIT; see [third-party notices](THIRD-PARTY-NOTICES.md). 原版美术参考不属于MIT授权。
