# Current EZapi release and verification

Current prerelease: **0.1.73-preview.4**, assembly **0.1.73**, license **MIT**. Framework source, tests and build tooling are public. Players install one Mods/EZapi.dll; the developer package adds optional tools, validator, guides and game-entry automation. Debug defaults off.

| Scope | Current result |
| --- | --- |
| Source rebuild and binary packaging | Passed |
| Source-linked offline suites | 59 passed |
| Separate validator CLI-process checks | 25 passed |
| Reference feature coverage | 39 implemented groups; not full project completion or gameplay acceptance |
| Current-DLL native UI, mixed Mods, save/reload | **Acceptance remains pending** |

Runtime SHA256: `7E4AB675B0E3BD81C56EEC4606731A6880287549EB4E8E90A707BE18BD52BEEF`. The runtime is unchanged from preview.1/preview.2; this release corrects documentation. Offline tests use explicit boundary stand-ins and do not establish actual game behavior.

Start with [installation](install-and-debug.en.md), [source builds](build-from-source.en.md), [AI entry](AI-START-HERE.en.md), [exact API signatures](public-api.index.json) and [machine-readable release status](release-status.json).

The [historical record index](history/README.md) contains reports scoped to older binaries. The 415 en/zh checks belong to historical 0.1.72 DLL `4238A189B36CEC4C55E88CC70E05E9086EC935412B13CF701671405A12E63274`. Historical statements such as “current DLL passed” never apply to this release. Private historical logs, saves and audit artifacts are not distributed; unavailable references are labeled as historical identifiers rather than working downloads.
