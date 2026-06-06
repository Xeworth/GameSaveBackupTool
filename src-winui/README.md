# GSBT Native — Windows (WinUI 3)

C# / .NET 8 edition: scan game saves, backup with retention, compress (ZIP / 7-Zip), tray, auto-backup.

## Requirements

- Windows 10 1809+ (64-bit recommended)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — build and test from source
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

## Release publish

Self-contained `win-x64` output for smoke tests and the installer (`PublishTrimmed` is **off** — a trimmed build warns with IL2026 and often breaks settings/catalog/WinUI at runtime).

From `src-winui/`:

```bat
scripts\publish_release.bat
```

Or manually:

```bat
dotnet publish src\GSBT.WinUI\GSBT.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile=win-x64
```

Output: `src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`

`publish_release.bat` wipes stale `publish\` output, copies `Assets\` beside `gsbt.exe`, strips non-English locale folders, and runs `validate_publish.bat`. Run the published app before packaging:

```bat
src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\gsbt.exe
```

## Installer (`GSBT_Setup_*.exe`)

Prerequisites: successful `publish_release.bat` run **and** Inno Setup **6.5.4+** (system light/dark wizard).

From `src-winui/`:

```bat
scripts\publish_release.bat
installer\build_installer.bat
```

Output: `installer\output\GSBT_Setup_0.0.2.260606.exe` (version from `installer\GSBT_Setup.iss` — keep in sync with `AppAboutInfo.VersionDisplay`).

Override Inno path if needed:

```bat
set ISCC=C:\path\to\ISCC.exe
installer\build_installer.bat
```

Full installer options, QA steps, and layout: [installer/README.md](installer/README.md) · [docs/INSTALLER_PLAN.md](docs/INSTALLER_PLAN.md).

## Portable zip

Yes — a **portable** build is possible. Release publish is already **self-contained** (`.NET 8` + Windows App SDK bundled in the folder). Portable means: zip that folder, user extracts and runs `gsbt.exe`. No Python-style single file, but no separate runtime install either.

From `src-winui/` (after `publish_release.bat`):

```bat
scripts\package_portable.bat
```

Output: `installer\output\GSBT_Portable_0.0.2.260606.zip`

Or build **all** release assets in one go:

```bat
scripts\package_release.bat
```

That produces both the portable zip and the installer exe under `installer\output\` (gitignored).

## GitHub Releases (three “versions”)

| What users get | How it ships |
|----------------|--------------|
| **Source** | The repo itself — GitHub auto-attaches `Source code (zip/tar.gz)` to every release tag |
| **Portable** | Upload `GSBT_Portable_*.zip` from `installer\output\` |
| **Installer** | Upload `GSBT_Setup_*.exe` from `installer\output\` |

Typical flow:

1. Bump version in `AppAboutInfo.VersionDisplay` and `installer\GSBT_Setup.iss`
2. `scripts\package_release.bat`
3. Create a GitHub Release (tag e.g. `v0.0.2.260606`) and attach the two binaries

Web UI: repo → **Releases** → **Draft a new release** → pick tag → drag files into **Attach binaries**.

CLI (with [GitHub CLI](https://cli.github.com/)):

```bat
cd src-winui
gh release create v0.0.2.260606 ^
  installer\output\GSBT_Setup_0.0.2.260606.exe ^
  installer\output\GSBT_Portable_0.0.2.260606.zip ^
  --title "GSBT v0.0.2.260606" ^
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
| [docs/SANDBOX.md](docs/SANDBOX.md) | Optional developer sandbox (`-s`) |
| [docs/INSTALLER_PLAN.md](docs/INSTALLER_PLAN.md) | Installer layout and decisions |
| [installer/README.md](installer/README.md) | Build and QA the Inno Setup package |
| [../docs/winui/dev/RELEASE_CHECKLIST.md](../docs/winui/dev/RELEASE_CHECKLIST.md) | Pre-release engineering tasks |
| [../docs/winui/dev/CursorAgentGuide.md](../docs/winui/dev/CursorAgentGuide.md) | WinUI UX conventions for agents |
