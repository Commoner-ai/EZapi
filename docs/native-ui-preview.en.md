> Current development guide. See [release-status.json](release-status.json) for version and evidence boundaries. Layout preview does not verify processing, trade or saves.

# Native UI components and AI layout preview

Preview actual game `PixelWindow`, tags, text, buttons and inventory controls. Reload JSON without rebuilding the DLL. Preview buttons have no gameplay callback, empty inventories are locked, and the tool never delivers items or requests saving. It changes only its own window.

Start from [computer](examples/ui-preview/computer.json), [furnace-style](examples/ui-preview/furnace.json), or [aquaculture-style](examples/ui-preview/aquaculture.json). These are layout recipes, not copied machine logic, sprites or recipes. Native constructor usage was checked against the official LearnerMod example.

With the game closed, enable ordinary developer tools; regression fixtures are not needed. From the extracted SDK:

```powershell
./developer-tools/Set-EZapiDeveloperTools.ps1 -GameDir '<game root>'
./developer-tools/Set-EZapiUiPreview.ps1 -GameDir '<game root>' -LayoutPath './docs/examples/ui-preview/computer.json'
```

Enter a disposable store save, open EZapi tools, and click **UI preview**. Edit/stage the JSON and click again to reload. Export diagnostics and inspect `UserData/EZapi/diagnostics.json` → `uiPreview.current`, together with a screenshot from the same preview. Existing [entry/input automation](automation.en.md) handles repeated clicks; it focuses the desktop game window.

The report compares `Controls.Expected` and `Actual` logical boxes. `Inspection.Text` includes actual locale font, size, alignment, wrapping, required width/height, and `Overflows`. Enlarge boxes, shorten labels or enable wrapping where needed; do not shrink every label to an unreadable size. Check both English and Chinese, then close and verify `visible=false` and `retainedControls=0`. `last` is historical, while `current` is null after closing.

Nodes require unique IDs and a named kind: Row, Column, Frame, Label, Tag, Button, Inventory, Slot or Spacer. `horizontal` Start/Center/End means left/center/right. `vertical` means top/**middle**/bottom and defaults to Center. Rows use summed widths and maximum height; columns use maximum width and summed heights. Frame aligns one child inside a fixed box. `gap` and uniform `padding` accept 0..64 logical pixels. Fixed boxes never silently shrink their children.

Width/height zero auto-sizes groups and inventory; text requires explicit boxes. A native inventory cell is 16×16 **logical pixels**, before game scaling. Native tags require height >=16 in the current build. `fontSize` is optional: retain the native locale size by default; explicit values are bounded to 4..24, not a readability recommendation. `wrap` enables actual-font wrapping. `reverse` reverses row/column child order; it does not rotate text or select RTL. Do not use spaces, HTML or size tags to simulate alignment.

Text is `{ "chinese": "中文", "english": "English" }`, with missing-side fallback. Layouts accept no callbacks, save instructions, item grants or arbitrary script. Input is bounded to 64 KiB, 128 nodes and depth 12, with duplicate/unknown fields and invalid enums rejected. See [machine-readable index](native-ui-preview.index.json) and [schema](../schemas/native-ui.schema.json).

Production Mods can use the same public `NativeUiComponents.Row`, `Column`, `Center`, `Frame`, `Inset`, `Section`, `Label`, `Button` and `Spacer`. A control must have only one native parent; the caller owns window lifetime, inventory admission/binding, transactions and persistence. Existing `MachineLayoutApi` saved graphs are not migrated automatically. See the [independent DLL caller](examples/native-ui-dll/UiLayout.cs).

For an existing native window, `NativeUiPreviewApi.InspectWindow(window)` reads actual text geometry without restyling, cloning or clicking it. Call only with a current valid window in a loaded run, on the game thread; do not retain it across load/scene transitions. Reports include at most 256 text nodes. Font geometry and screenshots help layout review, but do not establish arbitrary-Mod compatibility, functionality, accessibility or artistic quality.

Suggested AI instruction: use native components and logical pixels; center mixed-height rows vertically and button captions on both axes; stage/reload a recipe, inspect the current actual-font report plus screenshot, and repeat in English/Chinese. Preserve stable IDs. Keep gameplay callbacks outside preview; wire the accepted layout into the owning factory and real inventory rules separately.

If layout.json is absent, the preview button opens the embedded computer template. All three SDK JSON recipes are embedded in the same DLL, so fixtures and distributed definitions stay identical.
