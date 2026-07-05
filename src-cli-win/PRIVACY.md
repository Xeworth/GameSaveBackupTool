# Privacy — Game Save Backup Tool (GSBT)

GSBT is a **local-first** Windows desktop app. It does not include analytics, advertising, or account sign-in. This document describes what data the app stores and what it sends over the network.

## Summary

| Topic | Behavior |
|--------|----------|
| **Telemetry** | None built in |
| **Account / cloud** | None |
| **Save file contents** | Read from your PC for backup; not uploaded |
| **Network** | Optional: Ludusavi manifest refresh (GitHub) only |

## Data stored on your PC

All persistent WinUI app data lives under:

`%AppData%\Game Save Backup Tool\winui\`

(Older builds used `%AppData%\GSBT\`; the app migrates that folder on first launch after the rename.)

Typical files:

| File / folder | Purpose |
|---------------|---------|
| `winui_settings.json` | UI preferences, backup folder path, compression options, auto-backup settings |
| `game_save_data.json` | Game names, detected save paths, registry hints, last-backup timestamps |
| `ludusavi-save-manifest.json` | Cached save-location index (from bundled copy and/or online refresh) |
| `ludusavi-save-manifest.meta.json` | Manifest cache metadata (e.g. ETag, last fetch time) |
| `backup_run_checkpoints\` | Per-backup **metadata** (paths, sizes, timestamps) — not save file contents |
| `logs\winui_last_error.txt` | Last crash / unhandled exception text (may include file paths) |

Ephemeral sandbox simulation data may appear under:

`%LocalAppData%\Game Save Backup Tool\gsbt\`

Your **actual backups** are written only to the backup folder you choose in Settings (default: `Documents\gsbt-backups\` unless you change it).

Installed edition files (including bundled `7z.dll`) live under:

`%ProgramFiles%\Game Save Backup Tool\`

## Network use

GSBT works offline for core features if a manifest is already on disk (the app ships a bundled manifest).

| When | Endpoint | Why |
|------|----------|-----|
| You choose **Download latest manifest and rescan** | `raw.githubusercontent.com` (Ludusavi manifest) | Update save-location database |

Compression uses a **bundled** `7z.dll` shipped with the app. GSBT does **not** download 7-Zip installers or phone home for compression.

No other third-party analytics or tracking endpoints are used by the application code reviewed for this document.

## What is not collected

- No usage analytics SDK
- No crash reporting service (only local `winui_last_error.txt` on failure)
- No upload of save games, registry exports, or backup archives to GSBT servers (there are no GSBT servers)

## Registry and file system access

- Reads installed-game hints (Steam, GOG Galaxy, uninstall registry, etc.) to build the game list
- Reads save folders you scan or assign
- May **export** registry subtrees you configure for registry-based saves (`.reg` files in your backup folder)
- May compress backups using bundled **7z.dll** (`.7z`) or built-in ZIP

These operations stay on your machine unless **you** copy backup files elsewhere.

## Sandbox / developer mode

Launching with `-s` or `GSBT_SANDBOX=1` enables extra developer UI (monitor, simulated games). That mode is optional and not required for normal backups. See [src-winui/docs/SANDBOX.md](src-winui/docs/SANDBOX.md).

## Your choices

- Turn off auto-backup and manifest refresh if you want minimal network use
- Choose backup and compression paths yourself
- Delete `%AppData%\Game Save Backup Tool\` (and legacy `%AppData%\GSBT\` if still present) and your backup folder at any time to remove local app data

## Contact

For privacy questions about this open-source project, open an issue on the GitHub repository listed in [README.md](README.md).

*Last updated: June 2026 — aligned with GSBT v0.1.2.*
