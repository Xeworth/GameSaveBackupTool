# GSBT CLI for Windows

`gsbt.exe` is the Windows terminal edition of Game Save Backup Tool. It scans installed PC games, resolves save folders, backs them up with retention, compresses backup data to `.7z`, and exposes stable `--ai` JSON output for agents and scripts.

This repository is intentionally **Windows-only** for now. The CLI targets `net10.0-windows10.0.19041.0`, uses Windows game/save discovery, reads Windows registry save locations, and ships the Windows `win-x64` native `7z.dll`.

## Relationship To The GUI

This CLI is the automation backbone for the WinUI desktop app:

- `gsbt.exe` is the terminal entry point.
- `gsbt-main.exe` is the WinUI GUI entry point in the full app package.
- CLI and GUI share catalog/settings under `%AppData%\Game Save Backup Tool\winui`.
- The CLI can run alone, then later live in the same install folder as the GUI.

Planned product flow:

1. Install the CLI-only package first.
2. Use `gsbt scan`, `gsbt list`, `gsbt backup`, and `gsbt compress` from a terminal or AI agent.
3. Run `gsbt status --ai` to detect whether the GUI is installed.
4. If the GUI is missing, run `gsbt get gui` to download the latest WinUI installer silently.
5. Once upgraded, `gsbt gui` opens the desktop experience.

## Quick Start

```powershell
gsbt status
gsbt scan
gsbt list
gsbt backup 2
gsbt backup trep, lego star, mafia def
gsbt compress
```

Targets can be row numbers, ranges, exact names, or fuzzy names:

```powershell
gsbt backup 1,3,5
gsbt backup 2-6
gsbt backup sons of the forest
gsbt backup lego star, lego batman, mafia def
```

## AI / Automation Mode

Use `--ai` for stable JSON, no progress UI, and no interactive prompts:

```powershell
gsbt help --ai
gsbt status --ai
gsbt list --ai
gsbt backup trep, ho, sons --ai
gsbt compress --ai
```

Recommended agent flow:

```powershell
gsbt status --ai
gsbt scan --ai
gsbt list --ai
gsbt backup --ai
gsbt compress --ai
```

## Build From Source

Requirements:

- Windows 10/11 x64
- .NET 10 SDK

```powershell
dotnet build .\gsbt-cli-win.slnx -c Debug
dotnet test .\gsbt-cli-win.slnx -c Debug --no-build
```

## Publish CLI-Only Package

```powershell
.\scripts\publish_cli.ps1
```

Output:

```text
src\GSBT.Cli\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

Package zip:

```powershell
.\scripts\package_cli.ps1
```

Output:

```text
artifacts\gsbt-cli-win.zip
```

## One-Line Install Script

Install the latest CLI from GitHub (uses `gsbt-cli-win*.zip`, or falls back to the portable release zip):

```powershell
irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-cli-win/scripts/install.ps1 | iex
```

During testing you can override the download URL:

```powershell
$env:GSBT_CLI_ZIP_URL = "https://github.com/Xeworth/GameSaveBackupTool/releases/download/v0.1.3.260619/gsbt-portable-0.1.3.260619.zip"
irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-cli-win/scripts/install.ps1 | iex
```

Install the WinUI GUI beside the CLI:

```powershell
gsbt get gui
```

Or install the full GUI package directly:

```powershell
irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex
```

## Local Install

```powershell
.\scripts\install_cli_local.ps1
```

Default install folder:

```text
%LocalAppData%\Game Save Backup Tool
```

That folder is added to the current user's PATH unless `-NoPath` is passed.

## Repository Layout

```text
src\GSBT.Cli          terminal app
src\GSBT.Core         shared catalog, scan, backup, compression logic
tests\GSBT.Core.Tests core behavior tests
data                  bundled Ludusavi save manifest
native\win-x64        bundled 7-Zip native library
scripts               build, package, local install helpers
```

## Notes

- This repo is not cross-platform yet.
- Linux/macOS support would require replacing Windows game detection, registry save support, Windows path assumptions, and native compression packaging.
- The full WinUI installer can include this same `gsbt.exe` in the same folder as `gsbt-main.exe`.
