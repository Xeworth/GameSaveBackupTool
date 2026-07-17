# GSBT Clean-Machine Release Checklist

Run this after building final release artifacts and before publishing a tag. Use disposable Windows 10 x64 and Windows 11 x64 machines without a .NET SDK or developer tools.

## Install And Identity

- [ ] Install the Full setup per-user without elevation.
- [ ] Confirm `gsbt-main.exe`, `gsbt.exe`, and `gsbt-sandbox.exe` start without a separate runtime.
- [ ] Confirm main uses the red-arrow icon and sandbox uses the blue-arrow icon in files, shortcuts, taskbar, and toasts.
- [ ] Open a new terminal and confirm `gsbt status` resolves from PATH.
- [ ] Install Compact and confirm backup/compression/verify/restore work while screen saver media is unavailable gracefully.

## CLI

- [ ] `gsbt status` and `gsbt status --ai` show `v0.3.2.260717`, GUI state, and manifest provenance.
- [ ] CLI-only help exposes `gsbt get gui`; full-install help exposes `gsbt gui`; neither exposes `gsbt update`.
- [ ] `gsbt help --ai` parses as one JSON object and lists all supported commands.
- [ ] `gsbt scan --full`, `gsbt list`, and typo suggestions behave correctly.
- [ ] Add a temporary custom game, back it up, run fast/full verify, and restore it to an alternate folder.
- [ ] Cancel a backup/compression and confirm prior snapshots/archive files remain usable.
- [ ] Export diagnostics and confirm personal paths are redacted.

## GUI

- [ ] First-run backup path choice is clear and persists.
- [ ] **Add save folder...** opens from a Not found row.
- [ ] Backup, compression, verification, and Restore preview/confirmation complete for a temporary test game.
- [ ] Disconnect/reconnect a removable backup root and confirm history is preserved.
- [ ] Auto-backup coalesces a burst of save writes into one completed snapshot.
- [ ] Tray, toast, settings persistence, system date format, and sandbox monitor work.

## Upgrade And Removal

- [ ] Install CLI-only, then run `gsbt get gui`; confirm settings and PATH survive.
- [ ] Cancel `gsbt get gui` after download begins and confirm the CLI-only install still starts.
- [ ] Run an in-place upgrade and confirm the reported/binary version changes.
- [ ] Uninstall and confirm application files/PATH entry are removed while user settings remain.

Record OS build, artifact filenames, test time, failures, and screenshots beside the release notes.
