> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Build EZapi from source

Current framework, validator, tests and tooling are published under the [MIT license](../LICENSE) starting with 0.1.73-preview.2. Older private repository history is not imported.

This page describes prerelease 0.1.87. Check out tag v0.1.87-preview.1 for its matching source.

Install .NET 8 SDK, Python 3.12+ and PowerShell. Offline verification requires no game files:

```powershell
git clone https://github.com/Commoner-ai/EZapi.git
cd EZapi
pwsh -NoProfile -File ./Build-EZapi.ps1 -OfflineOnly
```

This runs 67 source-linked suites, 25 separate CLI-process cases and capability catalog checks. Tests use explicit native-boundary stand-ins. They do not verify actual Unity UI, native calls, or saved-game behavior. Reports go to ignored work files.

For the runtime build also install .NET 6 SDK and supply your own Probably Stolen installation with compatible MelonLoader IL2CPP .NET6. Start it once to generate `MelonLoader/Il2CppAssemblies`. Current baseline: Demo050REV5-L. Game and loader DLLs are not redistributed:

```powershell
pwsh -NoProfile -File ./Build-EZapi.ps1 -GameDir '<your game directory>' -OutputDir './outputs/my-candidate'
python scripts/Package-EZapi.py --candidate outputs/my-candidate --output outputs/my-binary-packages
```

Use fresh output directories. The build compiles framework, examples, validator and metadata inspector, runs offline verification, regenerates and verifies indexes for that DLL, then creates a binary package candidate. It never starts or changes the game and does not install Mods. Rebuilt hashes can differ with toolchains and paths; native verification is never inherited from a previous DLL.

Source: `src/EZapi`, `src/EZapi.ContentValidator`, `src/EZapi.Sample`, `work/ezapi-*-tests` and `work/ezapi-inspect`. Start with the [AI entry](AI-START-HERE.en.md) and [domain source index](source.index.json). GitHub Actions runs offline checks without game files. Fetch official references separately using `scripts/Fetch-OfficialDocs.ps1` and respect their license.

Players still install one Mods/EZapi.dll from the game package. The developer package retains optional debug settings and [game-entry automation](automation.en.md). Automation runs only when explicitly invoked.
