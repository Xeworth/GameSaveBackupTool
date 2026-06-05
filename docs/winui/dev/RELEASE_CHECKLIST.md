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

- [ ] **7-Zip installer hash** — verify SHA-256 of pinned download before `Process.Start` (`SevenZipDownloadInstall.cs`)
- [ ] **reg.exe arguments** — use `ArgumentList` or strict subkey validation (`RegistrySaveBackupService.cs`)
- [ ] **Re-validate registry targets** before auto-backup from catalog JSON
- [ ] **Delete temp 7-Zip installer** in `finally` after install (`MainViewModel.InstallSevenZipFromOfficialSiteAsync`)

### Performance (biggest UX wins)

- [ ] **Integrity reconcile off UI thread** — `ReconcileLastBackupDiskIntegrity` compute on `Task.Run`, batch UI apply
- [ ] **Batch scan UI updates** — throttle per-game `UpsertFromResult`; dictionary index instead of `FirstOrDefault` per row
- [ ] **Defer backup-size column** refresh until idle or column visible (optional)

### Build / deploy hygiene

- [ ] **Remove unused packages** — `Microsoft.Web.WebView2`, `Serilog`, `Microsoft.Extensions.Logging.Abstractions`
- [ ] **Pin NuGet versions** in `src/GSBT.WinUI/GSBT.WinUI.csproj` and `src/GSBT.Core/GSBT.Core.csproj` (replace `1.*`, `8.*`, `9.*`)
- [ ] **Release smoke test** — self-contained `win-x64` publish: scan, backup, compress, tray, settings save
- [ ] **Disable `PublishTrimmed`** if smoke test fails; document in README
- [ ] **Gate sandbox seed assets** — `data/sandbox_simulation` only in dev publish profile (optional; assets are small today)
- [x] **Set GitHub URL** in `AppAboutInfo.SourceRepositoryUrl` and README release links

### Documentation (foundation)

- [x] `README.md`
- [x] `LICENSE` (MIT)
- [x] `PRIVACY.md`
- [x] `CHANGELOG.md`
- [x] `CONTRIBUTING.md`
- [x] [src-winui/docs/SANDBOX.md](../../../src-winui/docs/SANDBOX.md)
- [x] Repo layout (monorepo: `src-winui/`, edition stubs; deprecated root Python removed)

---

## Soon after v0.1

- [ ] **Manifest download integrity** — pinned hash or signed bundle; size/timeout limits (`LudusaviManifestProvider.cs`)
- [ ] **Cap / redesign auto-backup watchers** for 100+ save folders
- [ ] **Game folder name collision** — unique backup dir key or user warning before retention prune
- [ ] **Registry backup checkpoints** — manifest for `.reg` exports like folder backups
- [ ] **Junction / reparse point** guard on copy and compress
- [ ] **Restrict `compression_7z_path`** to trusted locations or publisher check
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

- Self-contained WinUI build is large (~150–250 MB) — expected for unpackaged WASDK
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

1. Fresh `%AppData%\GSBT` or backup existing
2. Scan → games appear; filter modes work
3. Backup one game + bulk backup; retention folder created
4. Compress backup root (ZIP and/or 7z if installed)
5. Auto-backup on + edit save file → backup fires
6. Minimize to tray; tray menu Show / Backup / Compress / Quit
7. Settings save survives restart
8. F1 shortcuts, F11 About, F12 Diagnostics

Installer build steps: [../../../src-winui/installer/README.md](../../../src-winui/installer/README.md).
