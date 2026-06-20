# Changelog

All notable changes to Game Save Backup Tool are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).  
Versioning: `0.MINOR.PATCH.YYMMDD` (see `AppAboutInfo.VersionDisplay`).

## [0.1.3.260619] — 2026-06-19

### Packaging & install

- **Per-user install** under `%LocalAppData%\Game Save Backup Tool\` (no admin; PowerToys-style layout). Start Menu shortcuts; optional desktop icons.
- **Self-contained publish** — .NET 8 + Windows App SDK bundled; no separate runtime install required.
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
