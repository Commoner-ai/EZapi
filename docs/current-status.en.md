# Current EZapi release and verification

Current prerelease **0.1.76-preview.1**, assembly **0.1.76**, MIT. Players install one `Mods/EZapi.dll`. The SDK adds the same DLL, XML comments, validator, guides, caller examples and game-entry automation; debug defaults off. Framework source is available separately on GitHub.

This runtime adds [native UI components and AI layout preview](native-ui-preview.en.md), bilingual developer tools and explicit disposable-run binding. Reload JSON into actual native controls and inspect real fonts, sizes, alignment, wrapping, overflow and logical boxes. Existing native windows can use read-only InspectWindow.

| Scope | Current result |
| --- | --- |
| Runtime projects and independent DLL consumers | Compilation checks |
| Source-linked offline suites | 60; plus 25 independent CLI-process checks |
| Reference feature coverage | 39 implemented groups; not full project or native acceptance |
| Scoped current-DLL UI and en/zh observations | 421 passed, 0 failed; see scopedNativeObservations |
| Full framework, mixed Mods and current-DLL save/reload | **Acceptance remains pending** |

Read [release-status.json](release-status.json) for the exact DLL hash and scoped evidence. Font and geometry checks do not establish processing, arbitrary-Mod compatibility or persistence. Transient fixtures run only in an explicitly bound disposable store and request no saving.

Start with [AI entry](AI-START-HERE.en.md), [exact signatures](public-api.index.json), [task workflows](developer-workflows.index.json), [installation](install-and-debug.en.md), [source builds](build-from-source.en.md) and [automation](automation.en.md). Optional [native Tooltip recipes](tooltip-style.en.md) and [art references](item-art.en.md) remain available.

The [historical record index](history/README.md) and old release notes apply only to their recorded DLLs. The 0.1.74 Wild feedback, unpublished 0.1.75 bilingual batch and 0.1.72 415-check batch never transfer to this build. Private raw logs, saves and internal audit artifacts are not distributed. Original artwork retains separate rights and is excluded from MIT.
