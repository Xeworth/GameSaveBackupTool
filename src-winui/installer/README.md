# GSBT installer (Inno Setup)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) — publish the app
- [Inno Setup 6.5.4+](https://jrsoftware.org/isinfo.php) — required for `WizardStyle=modern dynamic` (system light/dark theme)

## Build

From `src-winui/`:

```bat
scripts\package_release.bat
```

Or step by step:

```bat
scripts\publish_release.bat
scripts\package_portable.bat
installer\build_installer.bat
```

Output: `installer/output/`

| File | Description |
|------|-------------|
| `GSBT_Setup_<version>.exe` | Per-user installer (no admin) |
| `GSBT_Portable_<version>.zip` | Portable self-contained folder |

Override Inno path: `set ISCC=C:\path\to\ISCC.exe`

`publish_release.bat` deletes stale `publish\`, packs `data\screensaver.7z`, prunes non-English locales, creates sandbox aliases, and validates WinUI runtime files.

## Install location

| What | Path |
|------|------|
| **Application files** | `%LocalAppData%\Game Save Backup Tool\` |
| **Settings / catalog** | `%AppData%\Game Save Backup Tool\winui\` |
| **Screen saver cache** | Extracted from `data\screensaver.7z` into user data on first use |

Per-user install (`PrivilegesRequired=lowest`) — no UAC admin prompt. Users launch from **Start Menu**, not by browsing the install folder (same idea as PowerToys).

Uninstall removes `%LocalAppData%\Game Save Backup Tool\` but keeps `%AppData%\Game Save Backup Tool\` settings until the user deletes them.

## What gets bundled

| Component | In install / portable folder |
|-----------|------------------------------|
| **.NET 8 runtime** | Yes (self-contained publish) |
| **Windows App SDK** | Yes (`WindowsAppSDKSelfContained`) |
| **7z.dll**, app DLLs | Yes |
| **Screen saver media** | `data\screensaver.7z` only (no loose video/audio) |

The setup exe is large (~110–130 MB compressed) because it ships the full self-contained publish output. Many DLLs beside `gsbt.exe` are normal for unpackaged WinUI 3.

Only `en-us` locale folder ships (post-publish prune via `scripts/prune_publish_locales.ps1`).

## Install layout (under `%LocalAppData%\Game Save Backup Tool\`)

| File | Role |
|------|------|
| `gsbt.exe` | Main app |
| `7z.dll` | Bundled 7-Zip native library ([license](../../THIRD_PARTY.md)) |
| `gsbt-sandbox.exe` | Sandbox apphost (`gsbt-s.ico` embedded) |
| `gsbt-sandbox.pri` | WinUI PRI alias for sandbox exe |
| `branding\gsbt.ico` / `gsbt-s.ico` | Shortcut icons |
| `data\screensaver.7z` | Packed screen saver media |
| `data\ludusavi-save-manifest.json` | Bundled manifest seed |
| `en-us\` | English .NET satellite resources only |

## Installer options

| Page | Option | Default |
|------|--------|---------|
| Tasks | Desktop shortcut — main app | Unchecked |
| Tasks | Desktop shortcut — Sandbox tool | Unchecked |

Start Menu always includes **Game Save Backup Tool** and **GSBT Sandbox** shortcuts.

Alternative to the sandbox shortcut: run `gsbt.exe -s`.

The installer wizard is **English only** (`ShowLanguageDialog=no`) and follows Windows light/dark mode.

## Manual QA

1. Install on a clean profile — no admin prompt; files under `%LocalAppData%\Game Save Backup Tool\`.
2. `gsbt.exe` and `gsbt-sandbox.exe` launch from Start Menu shortcuts.
3. Install folder has `en-us` only (no `de`, `fr`, `ja`, …) and **no** `assets\video` or `Assets\video` (media is in `data\screensaver.7z`).
4. Compression screen saver plays after trigger (media extracts to user cache first time).
5. Uninstall removes LocalAppData install; `%AppData%\Game Save Backup Tool\winui` settings remain.
6. If launch fails: `%AppData%\Game Save Backup Tool\winui\logs\` or `%TEMP%\gsbt_winui_last_error.txt`.

## Portable zip

```bat
scripts\package_portable.bat
```

Self-contained `publish\` tree + `README.txt` (from `PORTABLE.txt`). Extract anywhere; settings still in `%AppData%\Game Save Backup Tool\winui\`.

## GitHub Release upload

1. Sync version: `AppAboutInfo.VersionDisplay` ↔ `#define MyAppVersion` in `GSBT_Setup.iss`
2. Run `scripts\package_release.bat`
3. Attach both files from `installer/output/`

```bat
gh release create v0.1.3.260619 ^
  installer\output\GSBT_Setup_0.1.3.260619.exe ^
  installer\output\GSBT_Portable_0.1.3.260619.zip ^
  --title "GSBT v0.1.3.260619" ^
  --notes-file ..\CHANGELOG.md
```

## Version bump

Sync `#define MyAppVersion` in `GSBT_Setup.iss` with `AppAboutInfo.VersionDisplay` (format: `0.0.2.YYMMDD`).
