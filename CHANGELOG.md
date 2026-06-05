# Changelog

All notable changes to **Game Save Backup Tool (GSBT)** are documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added (patch queue)

- Trusted 7-Zip discovery only under Program Files (`SevenZipLocator`); 5 MB cap on official installer download (`SevenZipDownloadInstall`)
- Tray backup skips hidden estimate dialog; toast when skipped; warnings still prompt after restoring window
- Taskbar progress + flash on complete; title bar shows backup/compress `%` (`MainWindowShellProgress.cs`)
- App icons: embedded `gsbt.ico`; `gsbt-s.ico` on main + sandbox monitor when launched with `-s` (`AppBrandingIcons.cs`)
- Inno Setup installer (always includes sandbox hard-link shortcut); publish validation scripts

### Planned (still pending)

- Final installer QA on tagged release build
- Optional: unify inline WinUI icon glyphs across sandbox views

### Added

- WinUI 3 port: scan, catalog, backup, compression, settings, tray, auto-backup
- `scripts/publish_release.bat`; `PublishTrimmed` off for release builds
- [src-winui/docs/INSTALLER_PLAN.md](src-winui/docs/INSTALLER_PLAN.md)
- Ludusavi manifest (bundled + optional GitHub refresh) — credit [Ludusavi](https://github.com/mtkennerly/ludusavi) / [manifest](https://github.com/mtkennerly/ludusavi-manifest)
- Core unit tests (`tests/GSBT.Core.Tests`)
- Release documentation: `README.md`, `LICENSE`, `PRIVACY.md`, `docs/winui/dev/RELEASE_CHECKLIST.md`

### Changed

- Footer **Tools** → **Help**; shortcuts dialog trimmed to hotkeys only
- Tray context menu: compact 28px items + outer flyout padding
- **Monorepo layout:** `src-winui/` (C#), `src-pyqt/`, `src-tui/` stubs at [GameSaveBackupTool](https://github.com/Xeworth/GameSaveBackupTool)
- Dev docs consolidated under `docs/winui/dev/`

### Removed

- **Deprecated root Python app** (`main.py`, `core/`, `ui/`, `config/`, etc.) — superseded by WinUI port

## [0.0.1.250605] - TBD

First GitHub release (planned).

- Self-contained `win-x64` publish + optional Inno Setup installer
- See [docs/winui/dev/RELEASE_CHECKLIST.md](docs/winui/dev/RELEASE_CHECKLIST.md) for smoke test steps
