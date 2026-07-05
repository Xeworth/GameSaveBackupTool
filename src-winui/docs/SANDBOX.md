# GSBT sandbox (optional developer mode)

The **sandbox** is for development and testing. End users who only want backups **do not need to run it**.

## Normal use

- Run `gsbt-main.exe` with no extra flags
- Sandbox monitor, simulated games, and benchmark UI stay hidden
- No `-s` argument, no `GSBT_SANDBOX=1` environment variable

## Enabling sandbox

| Method | Effect |
|--------|--------|
| `launch_sandbox.bat` (from `src-winui/`) or `scripts\launch_sandbox.bat` | Builds and runs with `-s` |
| `gsbt-main.exe -s` or `gsbt-sandbox.exe` | Opens **main window** and **sandbox monitor** (same process; not monitor-only) |
| `set GSBT_SANDBOX=1` | Same as `-s` when launching the exe |

**Packaging rule:** Any "GSBT Sandbox" installer entry must launch the sandbox-branded GUI apphost, or run `gsbt-main.exe -s`, against an existing **Main** install. Do not ship a build that opens only `SandboxMonitorWindow` with no main shell or no shared settings/theme with Main.

Sandbox features include:

- Live log hub and resource monitor
- Simulated child process with dummy games (`data/sandbox_simulation/`)
- Compression benchmark UI
- Overrides for compression UI simulation, checkpoint drift previews, etc.

## Installers (planned)

Full detail: [INSTALLER_PLAN.md](INSTALLER_PLAN.md).

1. **Main installer**: self-contained GUI as `gsbt-main.exe`, CLI as `gsbt.exe`, red-arrow `gsbt.ico`, works without sandbox.
2. **Sandbox entry**: `gsbt-sandbox.exe` copied from `gsbt-main.exe` with blue-arrow `gsbt-s.ico` embedded; Start Menu shortcut uses `gsbt-s.ico`; no second copy of the publish folder.

Today both modes ship in **one** build; sandbox is **runtime opt-in** (`-s` only).

## Simulation child

Advanced testing spawns a second process with isolated settings. See `SimulationChildLauncher` and `MainPage.SimulationChild.cs`. Not used in production backup workflows.

## Network in sandbox

- Manifest “Download latest and rescan” is **disabled** in the simulation window (avoids GitHub from dummy runs)
- Compression in simulation uses the same bundled `7z.dll` as the main app when the publish folder is intact
