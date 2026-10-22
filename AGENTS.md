# EZapi contributor and AI instructions

- EZapi is MIT open source. Start with docs/AI-START-HERE.en.md or its Chinese counterpart, then docs/build-from-source.en.md.
- Prefer official game APIs and the supplied source/domain/error indexes. Accepted declarations do not prove native catalog application.
- Keep player installation at one Mods/EZapi.dll. Debug options default off; examples and game automation are opt-in.
- Run Build-EZapi.ps1 -OfflineOnly for 60 source-linked suites and CLI checks. Runtime builds additionally require your own game and compatible MelonLoader assemblies. Never redistribute those assemblies.
- Distinguish offline checks, current-DLL gameplay evidence, and historical DLL evidence. A successful build is not a native save/load pass.
- DepartureApi owns entry UI and click dispatch. Calling Mods own destinations, inventory synchronization, visit consumption and saves.
- No credentials, personal paths, private logs, saves, decompiled game material or old private repository history in this repository.
- Package binary releases using scripts/Package-EZapi.py. Source is available separately; run scripts/Check-SourceDistribution.py before publishing source.

- 当前发布与验收状态统一以 docs/release-status.json 为准；当前指南与 docs/history 分开。运行 scripts/Verify-Documentation.py 检查版本、链接和历史验收边界；打包时会核对开发包。不得将旧 DLL 的 runtime/native 通过记录升级为当前验收。

- Optional original-game artwork references in docs/art-reference/ are explicitly included at the user’s request; preserve their separate rights notice and exclude them from MIT artwork licensing. Do not export extraction environments, game code, private files or unrelated game assets. Player package excludes reference images.
