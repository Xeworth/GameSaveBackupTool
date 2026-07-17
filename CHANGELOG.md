# Changelog

All notable changes to Game Save Backup Tool are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning: `0.MINOR.PATCH.YYMMDD` (canonical value in `VERSION`).

## [0.3.2.260717] - 2026-07-17

### Added

- An embedded machine-facing agent notebook is exposed through `gsbt help --ai`, with grounded compression behavior and a future-safe place for verified product knowledge.
- Compression plateau events provide `agentStatus`, stable `knowledgeRef` IDs, and a concise first-heartbeat `agentHint` for AI agents.
- A resumable CLI compression benchmark harness and maintainer guidance document chunky/smooth progress behavior.
- The agent notebook now explains GSBT's broader custom-folder scope, missing-data research order, safety boundaries, and a sourced Warcraft III custom-content example.
- `gsbt add custom ... --ai` and `--json` provide structured custom-entry results for agents and scripts.

### Changed

- AI compression progress suppresses repeated native percentages while preserving independent 15-second liveness heartbeats during callback silence.
- Release metadata, WinUI package identity, CLI mirror metadata, and GitHub examples are synchronized to one product version.
- CLI help describes custom entries as folder backups for saves, maps, mods, profiles, projects, and other user-selected data.
- The bundled Ludusavi manifest was refreshed from upstream commit `292f8876ad90a95358dc83e5aba0bef9a374c6ed` (2026-07-17), increasing the compiled Windows catalog from 12,555 to 12,719 named entries and from 10,272 to 10,434 Steam IDs.

### Fixed

- The redundant compression 0% event is no longer emitted after the start event.
- Large chunky batches can no longer appear silently hung to an AI agent when 7-Zip pauses at a coarse progress phase or archive finalization.
- Version-tag pushes now trigger the GitHub release packaging job as intended.
- Locked restores now remain deterministic for both normal solution builds and self-contained `win-x64` publishing.

## [0.3.1.260716] - 2026-07-16

### Changed

- CLI command discovery now reflects the installed product: CLI-only installs expose `gsbt get gui`, while full installs expose `gsbt gui`.
- The public `gsbt update` command was removed; GSBT releases remain deliberate feature-complete installs.
- Folder pickers share one window-owned initialization path, and context-menu pickers wait for their flyout to close before opening.

### Fixed

- **Add save folder...** can open reliably from a Not found row in packaged installs and now reports a useful HRESULT if Windows rejects the picker.

## [0.3.0.260712] - 2026-07-12

### Added

- Transactional retained backups with staging, full-copy verification, run IDs, free-space checks, reparse-point guards, and prune-after-success retention.
- Fast/full snapshot verification in Core, `gsbt verify`, and **Verify latest backup** in the GUI.
- Explicit folder and registry restore with preview, confirmation, pre-restore safety snapshots, staging, rollback journals, SHA-256 post-copy verification, and `gsbt restore --ai`.
- Version-aware `gsbt get gui`, rollback-protected CLI-to-GUI installation, `gsbt settings --ai`, and redacted `gsbt diagnostics` operation history.
- Manifest provenance in status/diagnostics, full rescans, semantic path limits, and last-known-good manifest handling.
- Windows CI, Dependabot, package lock files, one root `VERSION`, and a pinned .NET 10 SDK policy.

### Changed

- Auto-backup now debounces save bursts, retries transient locks, starts cooldown only after success, and caps live watchers.
- Backup, compression, restore, settings, catalog, and checkpoint writes coordinate across GSBT processes and use atomic file replacement.
- Windows App SDK is referenced through the WinUI and Runtime component packages, removing unused AI/ML/Widgets payloads and reducing the self-contained publish by about 53 MB.
- The installer offers **Full** and **Compact** types; Compact omits only compression screen saver media.
- SharpSevenZip moved to 2.0.109 and the test suite moved to xUnit v3.

### Fixed

- Offline backup destinations no longer erase remembered backup history.
- Registry fingerprints report incomplete reads; registry restore rejects snapshots containing keys outside the requested subtree.
- Bundled manifests retain valid entries while unsafe legacy path templates are removed instead of causing an empty-catalog fallback.
- Failed/cancelled compression and GUI updates clean staging safely and preserve the previous working state.
- Sandbox blue-icon executable and PRI alias validation are enforced during release packaging.

### Deferred

- Final release hashes/download verification until the exact GitHub release assets are uploaded.
- Paid Windows code signing and public artifact attestations.

## [0.1.3.260619] — 2026-06-19

### Packaging & install

- **Per-user install** under `%LocalAppData%\Game Save Backup Tool\` (no admin; PowerToys-style layout). Start Menu shortcuts; optional desktop icons.
- **Self-contained publish** — .NET 10 + Windows App SDK bundled; no separate runtime install required.
- **Screen saver media** packed as `data\screensaver.7z` (not loose mp4/ogg in the install folder). Extracted to user cache on first use.
- **English-only locales** in release publish (`en-us` only; WinApp SDK MUI folders pruned after build).
- Portable zip (`GSBT_Portable_*.zip`) — same self-contained layout; extract anywhere and run `gsbt.exe`.

### Compression screen saver

- Screen saver IDs 1–4 with full-length video/audio tracks and themed progress bars.
- End-of-track rotation with fade transitions (video, audio, progress theme).
- Sandbox preview combo for IDs 1–4 and rotate mode.

### Compression

- Native `.7z` via bundled `7z.dll` (SharpSevenZip); solid archiving disabled for cancel reliability.
- Compression level slider shows MX tier labels.

### UI polish (0.1.3)

- Screen saver mute/exit hover consistent in light and dark themes; video frame uses `GsbtBorderBrush`.
- Diagnostics available only in sandbox (`-s`); removed from main app Help menu.
- Screen saver audio volume lowered; temporary resize allowed during screen saver when resolution is locked.


### Docs

- Installer, portable, and third-party attribution docs updated for new layout.
- Vendored InnoDependencyInstaller kept optional (not used in setup).

## [0.1.2] — earlier

- WinUI 3 native edition, Ludusavi manifest, backup retention, tray, auto-backup, sandbox dev mode (`-s`).

[0.1.3.260619]: https://github.com/Xeworth/GameSaveBackupTool/compare/v0.1.2...v0.1.3.260619
