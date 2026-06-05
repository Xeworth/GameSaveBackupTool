# GSBT installer (Inno Setup)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) — publish the app
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — compile `GSBT_Setup.iss`

## Build

From `src-winui/`:

```bat
scripts\publish_release.bat
installer\build_installer.bat
```

Output: `installer/output/GSBT_Setup_*.exe`

Override Inno path: `set ISCC=C:\path\to\ISCC.exe`

`publish_release.bat` deletes stale `publish\` output before building and validates WinUI runtime files (including `System.Runtime.InteropServices.dll` size). A corrupted publish folder is the most common cause of “installed app won't launch”.

## Install layout

| File | Role |
|------|------|
| `gsbt.exe` | Main app (Game Save Backup Tool) |
| `gsbt-sandbox.exe` | Hard link to `gsbt.exe` — same binary, sandbox session (main + monitor) |
| `gsbt-sandbox.pri` | Hard link to `gsbt.pri` (WinUI loads `{exe-name}.pri`) |
| `branding\gsbt.ico` / `gsbt-s.ico` | Shortcut icons |

The installer always creates the sandbox hard links (no extra disk space). A separate sandbox build that omits sandbox UI code is a future packaging goal — today the sandbox lives in the same binary.

## Installer options

| Page | Option | Default |
|------|--------|---------|
| Tasks | Desktop icon — main app | Unchecked |
| Tasks | Desktop icon — GSBT Sandbox | Unchecked |

Start Menu always includes **Game Save Backup Tool** and **GSBT Sandbox** shortcuts.

Alternative to the sandbox shortcut: run `gsbt.exe -s`.

## Manual QA

1. Install — `gsbt.exe` and `gsbt-sandbox.exe` both exist; both Start Menu shortcuts work.
2. Uninstall removes `{app}`; `%AppData%\GSBT` settings remain.
3. If launch fails, check `%AppData%\GSBT\logs\winui_last_error.txt` or `%TEMP%\gsbt_winui_last_error.txt` — the app shows a dialog when a managed startup error occurs.

## Portable zip

After `publish_release.bat`:

```bat
scripts\package_portable.bat
```

Output: `installer/output/GSBT_Portable_<version>.zip`

The zip contains the full self-contained `publish\` folder (extract anywhere, run `gsbt.exe`). Settings still live in `%AppData%\GSBT`. A short `README.txt` is included.

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
3. On GitHub: **Releases** → **Draft a new release** → tag `v0.0.1.250605` → attach both files from `installer/output/`
4. Publish release

CLI example:

```bat
gh release create v0.0.1.250605 ^
  installer\output\GSBT_Setup_0.0.1.250605.exe ^
  installer\output\GSBT_Portable_0.0.1.250605.zip ^
  --title "GSBT v0.0.1.250605" ^
  --notes "First public WinUI release."
```

## Version bump

Sync `#define MyAppVersion` in `GSBT_Setup.iss` with `AppAboutInfo.VersionDisplay` in the WinUI project.
