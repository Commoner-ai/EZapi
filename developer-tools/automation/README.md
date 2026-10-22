> Current API guide. See [release status]../../docs/current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Game entry automation

The developer package includes `developer-tools/automation/Enter-EZapiTestStore.ps1` and its `Invoke-EZapiNativeTest.ps1` dependency. They launch/bind one game process, load the most recent save, open the developer panel, export fresh diagnostics and check the expected slot. No extra game DLL is required.

Enable the public developer panel first. Use a disposable recent save, the verified050REV5-L menu layout and a1920x1080 game window at screen origin. Run from the developer package root:

```powershell
./developer-tools/automation/Enter-EZapiTestStore.ps1 -GameDir '<game root>' -ExpectedSlot <test slot number> -EvidenceDir './outputs/entry-check'
```

ExpectedSlot is mandatory. The script loads the recent save before checking its slot, so select that save deliberately. A mismatch stops before regression/save actions. Existing workshop/guide modals must be closed. This does not create a new save or request saving. Normal clicks use process/focus/geometry checks without image recognition. Add -CaptureResult for a final screenshot. Failure captures evidence without blind input retries.

The batch runner requires both developer settings enabled and regressionFixtures=true, followed by restart. It reuses a single PID for selected groups. Locale regression retains internal slot/run bindings; maintainers must adapt those fixtures before use on another test save. Internal save/force-close plugins are excluded. The optional heldProbeName input parameter relies on an internal plugin and is not available to normal users; ordinary entry/click/drag/screenshot does not need it.

Input automation focuses the game and moves the pointer. Do not run it while using the desktop for other tasks. Keep scripts together; reports, logs and screenshots are local test evidence, not automatic proof of gameplay success.
