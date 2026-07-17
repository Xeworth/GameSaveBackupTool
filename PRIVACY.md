# Privacy - Game Save Backup Tool (GSBT)

GSBT is a local-first Windows desktop app. It does not include analytics, advertising, account sign-in, or a GSBT cloud service.

## Summary

| Topic | Behavior |
|-------|----------|
| Telemetry | None built in |
| Account or cloud | None |
| Save file contents | Read locally for backup, verification, compression, and an explicitly confirmed restore; not uploaded |
| Network | Optional Ludusavi manifest refresh and explicit CLI-to-GUI installation through GitHub |

## Data Stored On Your PC

Persistent app data lives under `%AppData%\Game Save Backup Tool\winui\`. Older `%AppData%\GSBT\` data is migrated on first launch.

| File or folder | Purpose |
|----------------|---------|
| `winui_settings.json` | UI preferences, paths, compression, and auto-backup settings |
| `game_save_data.json` | Game names, detected save/registry locations, and last-backup timestamps |
| `game_save_data.json.meta.json` | Catalog schema and writer-version metadata |
| `ludusavi-save-manifest.json` | Validated cached save-location index |
| `ludusavi-save-manifest.meta.json` | Manifest source/ETag/fetch metadata |
| `backup_run_checkpoints\` | Snapshot paths, sizes, timestamps, and content hashes; not save contents |
| `logs\operations.ndjson` | Local backup/compress/verify/restore/GUI-install outcomes |
| `logs\winui_last_error.txt` | Last local crash text, which may include file paths |

Ephemeral sandbox data may appear under `%LocalAppData%\Game Save Backup Tool\gsbt\`.

Actual backups are written only to the folder selected by the user. The first-run suggestion is `Documents\gsbt-backups\`. Installed application files normally live under `%LocalAppData%\Game Save Backup Tool\`.

## Network Use

Core backup, verification, compression, and restore work offline when a local manifest is available.

| User action | Endpoint | Purpose |
|-------------|----------|---------|
| Download latest manifest and rescan | `raw.githubusercontent.com` | Download the Ludusavi manifest |
| `gsbt get gui` | `api.github.com`, `github.com`, and GitHub release asset hosts | Resolve and download the full GSBT installer |
| IRM installation script | `raw.githubusercontent.com`, GitHub API, and GitHub release asset hosts | Download and run a selected GSBT package |

Custom GUI installer hosts are rejected by default. The CLI requires an explicit `--allow-custom-host` override for a non-GitHub HTTPS URL.

Compression uses the bundled `7z.dll`; GSBT does not download 7-Zip. There are no analytics or tracking endpoints.

## File System And Registry Access

- Reads installed-game hints from local launchers, folders, and Windows registry locations.
- Reads save folders selected or detected for backup and verification.
- Exports configured registry save keys to `.reg` files.
- Compresses backup data into `.7z` archives.
- Restores only after an explicit preview and confirmation. Folder restore uses staging, rollback, and a pre-restore safety snapshot. Registry restore creates a safety export first.
- A redacted diagnostics export omits stored backup/save paths from its report.

These operations stay on the local machine unless the user moves or uploads the resulting files.

## What Is Not Collected

- No usage analytics SDK
- No remote crash-reporting service
- No upload of saves, registry exports, archives, manifests, settings, or diagnostics to GSBT servers

## User Choices

- Keep manifest refresh and GUI installation offline by not invoking them.
- Turn off auto-backup.
- Choose and move backup data independently of GSBT.
- Delete `%AppData%\Game Save Backup Tool\`, the LocalAppData installation/cache, and the selected backup folder.

For privacy questions, open an issue in the repository listed in [README.md](README.md).

Last updated: July 2026, aligned with GSBT v0.3.2.
