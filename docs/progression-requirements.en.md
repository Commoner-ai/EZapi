> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Knowledge-base expansion: progression and requirements

`ProgressionApi.ReadUpgrade(id)` and `ReadService(id)` capture managed snapshots from the current loaded run. Upgrade price, unlocked/ready/prerequisite/demo-lock states and cooldown are distinct. Service introduced/unlocked/using/active/cooldown states are also distinct. `Ready` is a successful observation, not payment or permission to skip the native purchase path. Invalid input, missing lookup and unavailable reads have separate statuses. Native lookup exceptions are unavailable, not proof of absence.

These queries require the known game build and loaded-run main thread. They retain no native object, initialize no directory, and call no unlock, payment, cost update or repair operation. Snapshots carry run/slot/load-epoch identity and do not update automatically.

`RequirementApi.Evaluate` reads the current game locale and evaluates a shared C#/JSON definition. Supported kinds are `all`, `any`, `not`, `upgrade-unlocked`, `upgrade-ready`, `service-unlocked`, `service-introduced`, `service-active`, `service-off-cooldown`, `day-at-least`, `day-at-most` and `credits-at-least`. Target leaves use `targetId`; numeric leaves use `amount`. See the compiled `ProgressionExamples.cs` sample and `examples/requirements.json`.

Results are three-valued: Satisfied, Unsatisfied or Unavailable. NOT preserves Unavailable. A missing target cannot unlock an inverted condition. ALL is false if a known child is false; ANY is true if a known child is true. Unknown child evidence remains in the result. Empty ALL means no restrictions, but the native entrypoint still requires a loaded run.

The complete tree is copied and validated before reading any facts: max128 nodes, depth8,32 children per group,64KiB JSON. Cycles, unknown kinds, null/duplicate/unknown JSON fields and mixed leaf/group arguments reject. Repeated facts are resolved once per evaluation; run identity is checked again before return. Nothing is paid, unlocked or saved.

`Requirements.Evaluate(definition, facts, locale)` is the pure managed entrypoint for controlled custom fact providers and offline tools. Each `Message` is typed Chinese/English text with fallback; `Reason` is a developer diagnostic. Native upgrade titles use the game's localized getter; a service's `NativeDisplayName` is returned literally and may be a key or fixed text.

The knowledge-base scenarios are references. Actual signatures are checked against the current050REV5-L game assembly; native qualification and pricing remain authoritative. Scheduling, quest rewards, save migration and purchase implementation are outside these new read APIs.
