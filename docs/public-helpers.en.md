# EZapi public helpers

`new ModLog("author.tools", LogLevel.Info)` creates an independent per-mod logger. `Debug`, `Info`, `Warn` and `Error` honor the instance threshold; `Callback` is Debug-level tracing and `Off` disables output. The default backend routes to MelonLoader. An optional `Action<LogEntry>` sink receives immutable structured entries for files or tests. No global preferences are changed. `Write(level, () => message)` evaluates the formatter only when enabled. Formatter and sink errors remain observable to the caller.

`LogLevels.TryParse` accepts case-insensitive English level names and Chinese equivalents. Check its boolean result; failure sets the out value to Info. Invalid enum values throw.

Use one `WindowInputCapture` per window. Call `Open(frame, mouseHeld)` and `Close(frame, mouseHeld)` at transitions, then use `BlocksPointer(frame, pointerInside, mouseHeld)` in your own input routing. A gesture started inside the window remains captured outside or after close until the release frame ends. Repeated queries retain the release barrier; repeated Close does not extend capture. A gesture originating outside is not permanently stolen merely by passing over the panel.

This is a managed state utility. It installs no global input patches, UI shields or keyboard hooks. Combine each window's result using OR while preserving native denial. Call `Reset` on focus loss, load or scene teardown. Frame numbers must be nonnegative and monotonic until Reset. Reset releases only this instance's state; it does not destroy native drag objects.

`ManufacturingTerminal.NormalizeItemId(id, aliases)` trims outer whitespace, keeps unrelated valid IDs unchanged, and maps only caller-supplied aliases to the EZapi terminal. Aliases are ordinal and case-sensitive, limited to64, and validated as a whole. No aliases are globally registered, no vendor IDs are auto-claimed, and no save is migrated.

See the compiled `PublicHelperExamples.cs` sample. Offline tests exercise the real helper implementations with an explicit MelonLoader sink boundary. A native smoke check is attached to the existing registration debug button; it does not imply real native window input behavior or save migration.
