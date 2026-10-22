# EZapi contributor and AI instructions

- EZapi is MIT open source. Start with docs/AI-START-HERE.en.md or its Chinese counterpart, then docs/build-from-source.en.md.
- Prefer official game APIs and the supplied source/domain/error indexes. Accepted declarations do not prove native catalog application.
- Keep player installation at one Mods/EZapi.dll. Debug options default off; examples and game automation are opt-in.
- Run Build-EZapi.ps1 -OfflineOnly for 59 source-linked suites and CLI checks. Runtime builds additionally require your own game and compatible MelonLoader assemblies. Never redistribute those assemblies.
- Distinguish offline checks, current-DLL gameplay evidence, and historical DLL evidence. A successful build is not a native save/load pass.
- DepartureApi owns entry UI and click dispatch. Calling Mods own destinations, inventory synchronization, visit consumption and saves.
- No credentials, personal paths, private logs, saves, decompiled game material or old private repository history in this repository.
- Package binary releases using scripts/Package-EZapi.py. Source is available separately; run scripts/Check-SourceDistribution.py before publishing source.
