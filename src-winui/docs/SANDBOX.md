# GSBT sandbox (optional developer mode)

The **sandbox** is for development and testing. End users who only want backups **do not need to run it**.

## Normal use

- Run `gsbt.exe` with no extra flags
- Sandbox monitor, simulated games, and benchmark UI stay hidden
- No `-s` argument, no `GSBT_SANDBOX=1` environment variable

## Enabling sandbox

| Method | Effect |
|--------|--------|
| `launch_sandbox.bat` (from `src-winui/`) or `scripts\launch_sandbox.bat` | Builds and runs with `-s` |
| `gsbt.exe -s` or `gsbt-sandbox.exe` | Opens **main window** and **sandbox monitor** (same process; not monitor-only) |
| `set GSBT_SANDBOX=1` | Same as `-s` when launching the exe |

**Packaging rule:** Any “GSBT Sandbox” installer entry must run the same binary with `-s` against an existing **Main** install. Do not ship a build that opens only `SandboxMonitorWindow` with no main shell or no shared settings/theme with Main.

Sandbox features include:

- Live log hub and resource monitor
- Simulated child process with dummy games (`data/sandbox_simulation/`)
- Compression benchmark UI
- Overrides for “7-Zip installed”, checkpoint drift previews, etc.

## Installers (planned)

Full detail: [INSTALLER_PLAN.md](INSTALLER_PLAN.md).

1. **Main installer** — self-contained app as `gsbt.exe`, `gsbt.ico`, works without sandbox.  
2. **Sandbox (optional installer task)** — hard link `gsbt-sandbox.exe` → `gsbt.exe`; Start Menu shortcut with `gsbt-s.ico`; **no second copy** of the publish folder.

Today both modes ship in **one** build; sandbox is **runtime opt-in** (`-s` only).

## Simulation child

Advanced testing spawns a second process with isolated settings. See `SimulationChildLauncher` and `MainPage.SimulationChild.cs`. Not used in production backup workflows.

## Network in sandbox

- Manifest “Download latest and rescan” is **disabled** in the simulation window (avoids GitHub from dummy runs)
- **Get 7-Zip** installer download is **disabled** in simulation; use the full app for real installs
