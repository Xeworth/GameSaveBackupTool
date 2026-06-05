# GSBT installer & packaging plan (v0.1+)

Owner intent (May 2026). Use this when implementing Inno Setup / WiX / MSIX and sandbox shortcuts.

## One installer, one runtime (v0.1)

| Entry | Installs | Start Menu |
|-------|----------|------------|
| **GSBT Setup** | Full `publish\` output under e.g. `Program Files\GSBT\` | **Game Save Backup Tool** → `gsbt.exe` |
| *(always included)* | Hard link `gsbt-sandbox.exe` → `gsbt.exe` (no second runtime, no size difference) | **GSBT Sandbox** → `gsbt-sandbox.exe`, icon `gsbt-s.ico` |

- **Main works alone at runtime** — normal backup/compress/tray without `-s`; sandbox UI stays hidden.
- **Sandbox shortcut** — same binary; `gsbt-sandbox.exe` is a hard link created at install time.
- **Future:** split sandbox UI code into a separate build or optional download once packaging can omit ~40% of the WinUI surface without shipping the same DLLs either way.
- **Do not ship** a standalone sandbox build that opens **only** the monitor window with no main shell. That was a bad experiment; production sandbox entry must always start the **main app window** and the **sandbox monitor** together (same process today via `-s` / `LaunchSandboxMonitor`).

## `-s` launch behavior (must preserve)

Equivalent to `launch_sandbox.bat` / `GSBT_SANDBOX=1`:

1. **Main window** — full `MainPage` backup UI (not optional).
2. **Sandbox monitor** — `SandboxMonitorWindow` alongside.
3. **Theme** — `ThemeBridge` syncs light/dark across both; changing theme in Main updates monitor chrome.

Regression test before any sandbox-only shortcut ships: both windows visible, theme toggle affects both, catalog/settings use real `%AppData%\GSBT` (not orphan monitor).

## Icons (`src-winui/branding/`)

| File | Use |
|------|-----|
| `gsbt.ico` | Main shortcut, default app icon, uninstall entry |
| `gsbt-s.ico` | Sandbox shortcut; **also apply to the main window** when the process was started with `-s` so taskbar/button grouping does not look like two unrelated apps |

**Taskbar / icon note:** Windows may group windows from one process under one icon. When launched via sandbox shortcut, set **both** windows (or at least the main shell) to use `gsbt-s.ico` so the user sees the sandbox branding for that session. Verify Alt+Tab and taskbar stacking during installer QA.

## File count in install directory

Users should interact via Start Menu, not by browsing `Program Files\GSBT\`.

- **v0.1:** Self-contained folder publish (trim **off** — trimmed build breaks JSON/WinUI).
- **Sandbox add-on:** Prefer shortcut-only; do not duplicate DLLs/locale folders.
- **Later (optional):** `PublishSingleFile`, MSIX, or `SatelliteResourceLanguages=en` to shrink visible clutter.

Locale folders (`en-US`, `el-GR`, …) in `publish\` are normal .NET satellites, not a broken build.

## Build input

From `src-winui/`:

```bat
scripts\publish_release.bat
```

Output: `src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`

Main installer copies that tree. Sandbox entry is `gsbt-sandbox.exe` (hard link to `gsbt.exe`; same as `-s`).

## Inno Setup (v0.1)

Single installer — see [`installer/README.md`](../installer/README.md) and `installer/GSBT_Setup.iss`.

- Copies the full self-contained `publish\` tree (~150–250 MB).
- Always creates `gsbt-sandbox.exe` / `gsbt-sandbox.pri` hard links (0 extra bytes).
- Optional desktop icon tasks only; no sandbox component toggle.

## Related docs

- [SANDBOX.md](SANDBOX.md) — runtime `-s` and dev simulation
- [../../docs/winui/dev/RELEASE_CHECKLIST.md](../../docs/winui/dev/RELEASE_CHECKLIST.md) — pre-release tasks and patch queue
