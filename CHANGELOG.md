# Changelog

All notable changes to **Game Save Backup Tool (GSBT)** are documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.0.2.260606] - 2026-06-06

First public WinUI release.

### Added

- WinUI 3 port: scan, catalog, backup, compression, settings, tray, auto-backup
- Click column headers to sort the game list (with selection preserved across sorts)
- `gsbt-sandbox.exe` shipped in install and portable packages (hard link or copy beside `gsbt.exe`)
- Inno Setup installer with system light/dark wizard theme (`WizardStyle=modern dynamic`)
- Portable zip (`GSBT_Portable_*.zip`) for extract-and-run use
- User data under `%AppData%\GSBT\winui\` (isolated from other editions)
- Trusted 7-Zip discovery under Program Files; 5 MB cap on official installer download
- Taskbar progress + flash on backup/compress complete
- App icons: `gsbt.ico` / `gsbt-s.ico` for main and sandbox sessions
- `scripts/publish_release.bat`, `scripts/package_release.bat`, publish validation
- Ludusavi manifest (bundled + optional GitHub refresh)
- Core unit tests (`tests/GSBT.Core.Tests`)
- Release documentation: `README.md`, `LICENSE`, `PRIVACY.md`

### Changed

- Scan dedup and catalog restore fixes (installed games no longer vanish after scan)
- Footer **Tools** → **Help**; shortcuts dialog trimmed to hotkeys only
- Tray context menu: compact 28px items
- **Monorepo layout:** `src-winui/` (C#), edition stubs for PyQt/TUI

### Removed

- Deprecated root Python app — superseded by WinUI port

## [0.0.1.250605] - (superseded, never released)

Internal pre-release version with incorrect date suffix. Use **0.0.2.260606** instead.
