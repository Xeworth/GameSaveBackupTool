# GSBT installer (Inno Setup)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) — publish the app
- [Inno Setup 6.5.4+](https://jrsoftware.org/isinfo.php) — required for `WizardStyle=modern dynamic` (system light/dark theme)

## Build

From `src-winui/`:

```bat
scripts\publish_release.bat
installer\build_installer.bat
```

Output: `installer/output/GSBT_Setup_*.exe`

Override Inno path: `set ISCC=C:\path\to\ISCC.exe`

`publish_release.bat` deletes stale `publish\` output before building, creates `gsbt-sandbox.exe` aliases, and validates WinUI runtime files. A corrupted publish folder is the most common cause of “installed app won't launch”.

## Install layout

| File | Role |
|------|------|
| `gsbt.exe` | Main app (Game Save Backup Tool) |
| `gsbt-sandbox.exe` | Copy of `gsbt.exe` apphost with `gsbt-s.ico` embedded (rcedit) |
| `gsbt-sandbox.pri` | Hard link or copy of `gsbt.pri` (WinUI loads `{exe-name}.pri`) |
| `branding\gsbt.ico` / `gsbt-s.ico` | Shortcut icons |
| `en-us\` | English .NET satellite resources only |

The installer ships sandbox aliases from publish output and verifies them post-install. A separate sandbox build that omits sandbox UI code is a future packaging goal — today the sandbox lives in the same binary.

## Installer options

| Page | Option | Default |
|------|--------|---------|
| Tasks | Desktop shortcut — main app | Unchecked |
| Tasks | Desktop shortcut — Sandbox tool | Unchecked |

Start Menu always includes **Game Save Backup Tool** and **GSBT Sandbox** shortcuts.

Alternative to the sandbox shortcut: run `gsbt.exe -s`.

The installer wizard is **English only** (`ShowLanguageDialog=no`) and follows Windows light/dark mode.

## Manual QA

1. Install — `gsbt.exe` and `gsbt-sandbox.exe` both exist; both Start Menu shortcuts work.
2. Wizard matches system theme (test on light and dark Windows).
3. License page shows readable MIT text (root `LICENSE`).
4. Uninstall removes `{app}`; `%AppData%\GSBT\winui` settings remain.
5. If launch fails, check `%AppData%\GSBT\winui\logs\` or `%TEMP%\gsbt_winui_last_error.txt`.

## Portable zip

After `publish_release.bat`:

```bat
scripts\package_portable.bat
```

Output: `installer/output/GSBT_Portable_<version>.zip`

The zip contains the full self-contained `publish\` folder (extract anywhere, run `gsbt.exe` or `gsbt-sandbox.exe`). Settings live in `%AppData%\GSBT\winui\`. A short `README.txt` is included.

## Build everything for a release

```bat
scripts\package_release.bat
```

Produces in `installer/output/` (gitignored):

| File | Use on GitHub Releases |
|------|------------------------|
| `GSBT_Setup_<version>.exe` | Installer — Start Menu shortcuts, uninstaller |
| `GSBT_Portable_<version>.zip` | Portable — extract and run, no install |

**Source code** does not need a manual upload — GitHub attaches `Source code (zip)` and `Source code (tar.gz)` automatically when you publish a release tag.

### Upload steps

1. Sync version: `AppAboutInfo.VersionDisplay` ↔ `#define MyAppVersion` in `GSBT_Setup.iss`
2. Run `scripts\package_release.bat`
3. On GitHub: **Releases** → **Draft a new release** → tag `v0.0.2.260606` → attach both files from `installer/output/`
4. Publish release

CLI example:

```bat
gh release create v0.0.2.260606 ^
  installer\output\GSBT_Setup_0.0.2.260606.exe ^
  installer\output\GSBT_Portable_0.0.2.260606.zip ^
  --title "GSBT v0.0.2.260606" ^
  --notes-file ..\CHANGELOG.md
```

## Version bump

Sync `#define MyAppVersion` in `GSBT_Setup.iss` with `AppAboutInfo.VersionDisplay` in the WinUI project (format: `0.0.2.YYMMDD`).
