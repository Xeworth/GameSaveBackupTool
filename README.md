# Game Save Backup Tool (GSBT)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Game Save Backup Tool** is a personal, solo project for finding PC game save locations, backing them up with retention, and optionally compressing archives. It is built in **C#** (WinUI edition) with help from [**Cursor**](https://github.com/getcursor/cursor).

**Repository:** [github.com/Xeworth/GameSaveBackupTool](https://github.com/Xeworth/GameSaveBackupTool)

## Save locations — thanks to Ludusavi

The game list and save-path database lean heavily on the community [**Ludusavi manifest**](https://github.com/mtkennerly/ludusavi-manifest) ([`manifest.yaml`](https://github.com/mtkennerly/ludusavi-manifest/blob/master/data/manifest.yaml)). GSBT ships a bundled copy for offline use and can optionally refresh it from GitHub. Huge credit to [**Ludusavi**](https://github.com/mtkennerly/ludusavi) and everyone who maintains that data — this app would not be useful without it.

## What it does

These features are shared across GSBT editions (different UI “flavours,” same goals):

- **Scan** installed games and resolve save folders (Steam hints, uninstall registry, Ludusavi manifest)
- **Catalog** — persist your game list; add or rename entries manually
- **Backup** one game or many; timestamped folders with **retention** (keep N backups)
- **Registry saves** — export configured registry subtrees as `.reg` in your backup folder
- **Compress** backup roots (built-in ZIP and/or **7-Zip** when available)
- **Auto-backup** when save files change (where the platform supports file watching)
- **Integrity hints** — warnings when on-disk backups drift from the catalog
- **Settings** — backup folder, compression options, filters, notifications (edition-dependent UI)

Platform-specific extras (WinUI today): system tray, startup with Windows, optional developer **sandbox** monitor (`-s`).

## Editions

Same idea, different UI stacks — only **Native (WinUI)** is release-ready today; the others are placeholders for future flavours.

| Folder | Edition | Status |
|--------|---------|--------|
| [**src-winui/**](src-winui/) | **Native** — C# / WinUI 3 | Active; primary release target |
| [**src-pyqt/**](src-pyqt/) | **Basic** — Python PyQt GUI | Planned; source not in repo yet |
| [**src-tui/**](src-tui/) | **Lite** — Python Textual / TUI | Planned; source not in repo yet |

## Quick start (Windows Native)

From `src-winui/`:

```bat
launch.bat
```

Build, test, publish, and installer steps: [src-winui/README.md](src-winui/README.md).

**Requirements:** Windows 10 1809+ · 64-bit recommended · self-contained publish is typically **~150–250 MB** (Windows App SDK + .NET bundled).

**First release builds are unsigned** — Windows SmartScreen may warn until the file gains reputation. See [PRIVACY.md](PRIVACY.md) for what the app touches on your PC.

## Documentation

| Document | Description |
|----------|-------------|
| [PRIVACY.md](PRIVACY.md) | Local data and network use |
| [CHANGELOG.md](CHANGELOG.md) | Version history |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Issues and pull requests |
| [docs/winui/CODEBASE_OVERVIEW.md](docs/winui/CODEBASE_OVERVIEW.md) | Architecture notes and pulse check (optional) |
| [src-winui/docs/SANDBOX.md](src-winui/docs/SANDBOX.md) | Optional dev sandbox (`-s`) |
| [src-winui/installer/README.md](src-winui/installer/README.md) | Build the Inno Setup installer |

Contributor engineering checklists live under [docs/winui/dev/](docs/winui/dev/).

## License

[MIT](LICENSE) — Copyright © 2026 Xeworth
