# Privacy — Game Save Backup Tool (GSBT)

GSBT is a **local-first** Windows desktop app. It does not include analytics, advertising, or account sign-in. This document describes what data the app stores and what it sends over the network.

## Summary

| Topic | Behavior |
|--------|----------|
| **Telemetry** | None built in |
| **Account / cloud** | None |
| **Save file contents** | Read from your PC for backup; not uploaded |
| **Network** | Optional: Ludusavi manifest (GitHub), 7-Zip installer (7-zip.org) |

## Data stored on your PC

All persistent app data lives under:

`%AppData%\Roaming\GSBT\`

Typical files:

| File / folder | Purpose |
|---------------|---------|
| `winui_settings.json` | UI preferences, backup folder path, compression options, auto-backup settings |
| `game_save_data.json` | Game names, detected save paths, registry hints, last-backup timestamps |
| `ludusavi-save-manifest.json` | Cached save-location index (from bundled copy and/or online refresh) |
| `ludusavi-save-manifest.meta.json` | Manifest cache metadata (e.g. ETag, last fetch time) |
| `backup_run_checkpoints\` | Per-backup **metadata** (paths, sizes, timestamps) — not save file contents |
| `logs\winui_last_error.txt` | Last crash / unhandled exception text (may include file paths) |

Your **actual backups** are written only to the backup folder you choose in Settings (default under your user profile unless you change it).

## Network use

GSBT works offline for core features if a manifest is already on disk (the app ships a bundled manifest).

| When | Endpoint | Why |
|------|----------|-----|
| You choose **Download latest manifest and rescan** | `raw.githubusercontent.com` (Ludusavi manifest) | Update save-location database |
| You choose **Get 7-Zip** in Settings | `7-zip.org` | Download pinned 7-Zip installer (with your consent) |

No other third-party analytics or tracking endpoints are used by the application code reviewed for this document.

## What is not collected

- No usage analytics SDK
- No crash reporting service (only local `winui_last_error.txt` on failure)
- No upload of save games, registry exports, or backup archives to GSBT servers (there are no GSBT servers)

## Registry and file system access

- Reads installed-game hints (Steam, uninstall registry, etc.) to build the game list
- Reads save folders you scan or assign
- May **export** registry subtrees you configure for registry-based saves (`.reg` files in your backup folder)
- May run **7-Zip** or built-in ZIP when you compress backups

These operations stay on your machine unless **you** copy backup files elsewhere.

## Sandbox / developer mode

Launching with `-s` or `GSBT_SANDBOX=1` enables extra developer UI (monitor, simulated games). That mode is optional and not required for normal backups. See [src-winui/docs/SANDBOX.md](src-winui/docs/SANDBOX.md).

## Your choices

- Turn off auto-backup and manifest refresh if you want minimal network use
- Choose backup and compression paths yourself
- Delete `%AppData%\GSBT\` and your backup folder at any time to remove local app data

## Contact

For privacy questions about this open-source project, open an issue on the GitHub repository listed in [README.md](README.md).

*Last updated: June 2026 — aligned with GSBT v0.0.1 pre-release.*
