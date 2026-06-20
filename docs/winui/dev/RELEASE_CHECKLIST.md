# GSBT release checklist

Track pre-release engineering work for the first GitHub release.  
**UI refinements** from `todo.txt` are done; use this file for everything else.

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done

---

## Before tagging v0.1 (recommended)

### Reliability

- [ ] **Auto-backup cooldown** — set `_lastBackupUtc` only after a successful backup (`AutoBackupWatcherService.cs`; folder + registry paths)
- [ ] **Atomic settings write** — temp file + rename for `winui_settings.json` (`SettingsStore.cs`)
- [ ] **Atomic catalog write** — temp file + rename for `game_save_data.json` (`SaveCatalogManager.cs`)
- [ ] **Failed folder backup cleanup** — remove partial `{Game} - Backup {timestamp}` on error (`SaveFolderBackupService.cs`)
- [ ] **Bulk backup feedback** — toast or summary when batch backup has failures (`MainViewModel.BackupGamesAsync`)

### Security (proportionate for v0.1)

- [x] **Bundled `7z.dll`** — native compression ships beside `gsbt.exe`; license in [THIRD_PARTY.md](../../../THIRD_PARTY.md) (removed external 7-Zip installer flow)
- [ ] **reg.exe arguments** — use `ArgumentList` or strict subkey validation (`RegistrySaveBackupService.cs`)
- [ ] **Re-validate registry targets** before auto-backup from catalog JSON

### Performance (biggest UX wins)

- [ ] **Integrity reconcile off UI thread** — `ReconcileLastBackupDiskIntegrity` compute on `Task.Run`, batch UI apply
- [ ] **Batch scan UI updates** — throttle per-game `UpsertFromResult`; dictionary index instead of `FirstOrDefault` per row
- [ ] **Defer backup-size column** refresh until idle or column visible (optional)

### Build / deploy hygiene

- [ ] **Remove unused packages** — `Microsoft.Web.WebView2`, `Serilog`, `Microsoft.Extensions.Logging.Abstractions`
- [ ] **Pin NuGet versions** in `src/GSBT.WinUI/GSBT.WinUI.csproj` and `src/GSBT.Core/GSBT.Core.csproj` (replace `1.*`, `8.*`, `9.*`)
- [ ] **Release smoke test** — self-contained `win-x64` publish: scan, backup, compress, tray, settings save; installer on a clean VM (no separate .NET install)
- [ ] **Disable `PublishTrimmed`** if smoke test fails; document in README
- [ ] **Gate sandbox seed assets** — `data/sandbox_simulation` only in dev publish profile (optional; assets are small today)
- [x] **Set GitHub URL** in `AppAboutInfo.SourceRepositoryUrl` and README release links

### Documentation (foundation)

- [x] `README.md`
- [x] `LICENSE` (MIT)
- [x] `PRIVACY.md`
- [x] `CHANGELOG.md`
- [x] `CONTRIBUTING.md`
- [x] [THIRD_PARTY.md](../../../THIRD_PARTY.md) — `7z.dll` / SharpSevenZip LGPL attribution
- [x] [src-winui/docs/SANDBOX.md](../../../src-winui/docs/SANDBOX.md)
- [x] Repo layout (monorepo: `src-winui/`, edition stubs; deprecated root Python removed)

---

## Soon after v0.1

- [ ] **Manifest download integrity** — pinned hash or signed bundle; size/timeout limits (`LudusaviManifestProvider.cs`)
- [ ] **Cap / redesign auto-backup watchers** for 100+ save folders
- [ ] **Game folder name collision** — unique backup dir key or user warning before retention prune
- [ ] **Registry backup checkpoints** — manifest for `.reg` exports like folder backups
- [ ] **Junction / reparse point** guard on copy and compress
- [x] ~~**Restrict `compression_7z_path`**~~ — N/A; compression uses bundled `7z.dll` only
- [ ] **Optional separate sandbox release** zip (~same runtime; without sandbox simulation assets)

---

## Later / maintainability

- [ ] Split `MainViewModel` into focused services (scan / backup / integrity)
- [ ] Split `MainPage` partials further or extract backup-prompt module
- [ ] Optional `#if SANDBOX` or second project for dev monitor binary
- [ ] `ItemsRepeater` or lighter row template if 500+ games reported slow
- [ ] Streaming manifest index if memory becomes an issue

---

## Known limitations (document in release notes)

- Self-contained publish: binaries under `%LocalAppData%\Game Save Backup Tool\` (~180–220 MB unpacked). Locale folders pruned to `en-us`. Screen saver in `data\screensaver.7z`.
- Ludusavi manifest refresh requires internet; bundled manifest works offline
- HKLM registry exports may need elevation
- Sanitized game names can collide in edge cases (see audit)
- `PublishTrimmed` may break WinUI until fully tested

---

## Completed UI refinements (May 2026)

- [x] Tray context menu compact item height (28px)
- [x] Tray menu outer padding (presenter only)
- [x] Footer **Help** flyout (was Tools); no menu ellipses
- [x] Shortcuts dialog — keyboard shortcuts only
- [x] Workspace `dotnet clean` + legacy root folder removal
- [x] Professional repo layout (`src/`, `tests/`, `scripts/`)

---

## Release smoke test script (manual)

1. Fresh `%AppData%\Game Save Backup Tool` or backup existing
2. Scan → games appear; filter modes work; GOG / Other platform labels where expected
3. Backup one game + bulk backup; retention folder created
4. Compress backup root (`.7z` via bundled `7z.dll`; taskbar progress during compress)
5. Auto-backup on + edit save file → backup fires
6. Minimize to tray; tray menu Show / Backup / Compress / Quit
7. Settings save survives restart
8. F1 shortcuts, F11 About; F12 Diagnostics (sandbox `-s` only)

Installer build steps: [../../../src-winui/installer/README.md](../../../src-winui/installer/README.md).
