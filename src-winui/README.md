# GSBT Native — Windows (WinUI 3)

C# / .NET 10 edition: scan game saves, backup with retention, compress (ZIP / bundled `.7z`), tray, auto-backup.

## Requirements

- Windows 10 1809+ (64-bit recommended)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — build and test from source
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — only needed to compile `GSBT_Setup_*.exe`

## Quick start

From this folder (`src-winui/`):

```bat
launch.bat
launch_fast.bat
launch_sandbox.bat
```

Equivalent via `scripts\`:

```bat
scripts\launch.bat
scripts\launch_fast.bat
scripts\launch_sandbox.bat
```

`launch_fast.bat` skips rebuild when the Debug exe already exists. `launch_sandbox.bat` passes `-s` (main window + sandbox monitor).

## Build and test

From `src-winui/`:

```bat
dotnet build GSBT.sln -c Debug -r win-x64
dotnet test GSBT.sln -c Debug
```

Clean local build output:

```bat
scripts\clean.bat
```

## CLI (`gsbt`)

Terminal edition in `src/GSBT.Cli/` — same catalog and settings as the WinUI app (`%AppData%\Game Save Backup Tool\winui\`).

### After install (recommended)

The installer puts both binaries in one folder (e.g. `%LocalAppData%\Game Save Backup Tool\`):

| File | Role |
|------|------|
| `gsbt.exe` | **Terminal CLI** — type `gsbt` in cmd/PowerShell (PATH task, on by default) |
| `gsbt-main.exe` | **Desktop GUI** — Start Menu shortcut |

They share `7z.dll`, manifest data, and your settings/catalog. No duplicate `dotnet run` needed.

```bat
gsbt list
gsbt scan
gsbt backup 3
gsbt compress
gsbt gui
```

### One-line install (GitHub Release required)

```powershell
irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex
```

Downloads the latest `*setup*.exe` from GitHub and runs it silently. Open a **new** terminal afterward so PATH updates apply.

Upgrade from an existing CLI install:

```powershell
gsbt get gui
```

### Development (before install)

Short wrapper (builds CLI, then runs `gsbt`):

```bat
scripts\gsbt.bat list
```

Or full `dotnet run`:

```bat
dotnet run --project src\GSBT.Cli\GSBT.Cli.csproj -- list
```

Release publish merges CLI into the WinUI publish folder (`scripts\publish_release.bat`).

Typical flow: **scan** → **list** (numbered table) → **backup** `2` or fuzzy name → **compress**.

Targets: row index (`6`), lists (`1,3,5`), ranges (`2-5`), or game names (fuzzy; comma-separated for multiple). With no targets, **backup** / **compress** run on all eligible games.

Add `--json` to **list**, **backup**, or **compress** for machine-readable output.

Self-contained `win-x64` output for smoke tests, installer, and portable zip. `PublishTrimmed` is **off** — trimmed builds break settings/catalog/WinUI at runtime.

From `src-winui/`:

```bat
scripts\publish_release.bat
```

Or manually:

```bat
dotnet publish src\GSBT.WinUI\GSBT.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile=win-x64
```

Output: `src\GSBT.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`

`publish_release.bat` wipes stale `publish\`, packs screen saver media into `data\screensaver.7z`, strips non-English locale folders, copies WinUI `Assets\`, and runs `validate_publish.bat`. Debug builds still use loose `assets\video` and `assets\audio` beside the exe.

Run the published app before packaging:

```bat
src\GSBT.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\gsbt-main.exe
```

## Installer (`GSBT_Setup_*.exe`)

Prerequisites: successful `publish_release.bat` and Inno Setup **6.5.4+**.

From `src-winui/`:

```bat
scripts\package_release.bat
```

Or:

```bat
scripts\publish_release.bat
installer\build_installer.bat
```

Installs per-user to `%LocalAppData%\Game Save Backup Tool\` (no admin). Output: `installer\output\GSBT_Setup_*.exe` (version from `GSBT_Setup.iss` — sync with `AppAboutInfo.VersionDisplay`).

Override Inno path if needed:

```bat
set ISCC=C:\path\to\ISCC.exe
installer\build_installer.bat
```

Full installer options, QA steps, and layout: [installer/README.md](installer/README.md) · [docs/INSTALLER_PLAN.md](docs/INSTALLER_PLAN.md).

## Portable zip

Yes — a **portable** zip is supported. Release publish is **self-contained** (`.NET 10` and Windows App SDK bundled in the folder). Extract and run `gsbt-main.exe` (GUI) or use `gsbt.exe` (CLI) from the same folder; no installer or runtime prerequisite.

From `src-winui/` (after `publish_release.bat`):

```bat
scripts\package_portable.bat
```

Output: `installer\output\GSBT_Portable_<version>.zip`

Or build **all** release assets in one go:

```bat
scripts\package_release.bat
```

That produces both the portable zip and the installer exe under `installer\output\` (gitignored).

## GitHub Releases

| What users get | How it ships |
|----------------|--------------|
| **Source** | GitHub auto-attaches `Source code (zip/tar.gz)` to every release tag |
| **Portable** | `GSBT_Portable_*.zip` from `installer\output\` |
| **Installer** | `GSBT_Setup_*.exe` from `installer\output\` |

Typical flow:

1. Bump version in `AppAboutInfo.VersionDisplay` and `installer\GSBT_Setup.iss`
2. `scripts\package_release.bat`
3. Create a GitHub Release (tag e.g. `v0.2.0.260704`) and attach both binaries

```bat
cd src-winui
gh release create v0.2.0.260704 ^
  installer\output\GSBT_Setup_0.2.0.260704.exe ^
  installer\output\GSBT_Portable_0.2.0.260704.zip ^
  --title "GSBT v0.2.0.260704" ^
  --notes-file ..\CHANGELOG.md
```

Details: [installer/README.md](installer/README.md).

## Layout

| Path | Purpose |
|------|---------|
| `src/GSBT.Core/` | Scan, catalog, backup, compression (shared engine) |
| `src/GSBT.WinUI/` | WinUI application |
| `tests/GSBT.Core.Tests/` | Unit tests |
| `launch*.bat` | Quick entry points (delegate to `scripts/`) |
| `scripts/` | Launch, publish, validate, clean |
| `branding/` | `gsbt.ico`, `gsbt-s.ico` |
| `docs/` | Sandbox notes, installer plan |
| `installer/` | Inno Setup script (`GSBT_Setup.iss`) |

## Documentation

| Document | Description |
|----------|-------------|
| [../THIRD_PARTY.md](../THIRD_PARTY.md) | Bundled `7z.dll` and other licenses |
| [docs/SANDBOX.md](docs/SANDBOX.md) | Optional developer sandbox (`-s`) |
| [docs/INSTALLER_PLAN.md](docs/INSTALLER_PLAN.md) | Installer layout and decisions |
| [installer/README.md](installer/README.md) | Build and QA the Inno Setup package |
| [../docs/winui/dev/RELEASE_CHECKLIST.md](../docs/winui/dev/RELEASE_CHECKLIST.md) | Pre-release engineering tasks |
| [../docs/winui/dev/CursorAgentGuide.md](../docs/winui/dev/CursorAgentGuide.md) | WinUI UX conventions for agents |
