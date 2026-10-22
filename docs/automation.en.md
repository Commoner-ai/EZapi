> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Game entry automation

The developer package includes `developer-tools/automation/Enter-EZapiTestStore.ps1` and its `Invoke-EZapiNativeTest.ps1` dependency. They launch/bind one game process, load the most recent save, open the developer panel, export fresh diagnostics and check the expected slot. No extra game DLL is required.

Enable the public developer panel first. Use a disposable recent save, the verified050REV5-L menu layout and a1920x1080 game window at screen origin. Run from the developer package root:

```powershell
./developer-tools/automation/Enter-EZapiTestStore.ps1 -GameDir '<game root>' -ExpectedSlot <test slot number> -ExpectedVersion '<diagnostic version>' -EvidenceDir './outputs/entry-check'
```

ExpectedSlot and ExpectedVersion are mandatory; neither defaults to a personal slot or stale version. The script loads the recent save before checking its slot, so select that save deliberately. A mismatch stops before regression/save actions. Existing workshop/guide modals must be closed. This does not create a new save or request saving. Normal clicks use process/focus/geometry checks without image recognition. Add -CaptureResult for a final screenshot. Failure captures evidence without blind input retries.

The batch runner requires both developer settings enabled and regressionFixtures=true, followed by restart. It reuses a single PID for selected groups. Locale regression reads an explicit disposable-run binding from `UserData/EZapi/regression-session.json`. With the game closed, run `Set-EZapiDeveloperTools.ps1 -GameDir '<game root>' -EnableRegressionFixtures -RegressionSlot <slot> -RegressionRunId '<D-format run GUID>'`, then restart. Normal developer tools do not enable regression fixtures. Obtain the exact slot and RunId from fresh diagnostics.

Run `Invoke-EZapiGuideLocalization.ps1` after entering the bound store and opening the developer panel. Required parameters are GameDir, a fresh EvidenceDir, ExpectedSlot, ExpectedRunId, ExpectedVersion (the diagnostics version string), and ExpectedDllSHA256 (the installed DLL hash). It binds fresh version/run/epoch/PID evidence and clicks once. It checks transient en/zh item, machine, reading, workshop, dialogue, phone and supplier fixtures, then restores the original language. Supplier arrival is explicitly fixture-dispatched, not natural arrival/payment/delivery. The optional historical disk fixture is skipped outside its own original test run; binding another run does not authorize persistent fixtures. Internal save/force-close plugins are excluded. The optional heldProbeName input parameter relies on an internal plugin and is not available to normal users; ordinary entry/click/drag/screenshot does not need it.

Input automation focuses the game and moves the pointer. Do not run it while using the desktop for other tasks. Keep scripts together; reports, logs and screenshots are local test evidence, not automatic proof of gameplay success.
