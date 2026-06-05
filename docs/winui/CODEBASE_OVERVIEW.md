# Codebase overview (pulse check)

High-level notes for contributors and curious readers. This is **not** required to use GSBT — see [README.md](../README.md) for that.

*Line counts are approximate source lines (excluding `bin/`, `obj/`, `publish/`). Measured June 2026.*

---

## How big is the codebase?

| Area | Lines (C#) | What it is |
|------|------------|------------|
| **GSBT.Core** (engine) | **~4,400** | Shared logic: scan, backup, compress, catalog |
| ↳ Scan / detect / catalog | ~1,880 | Finding games, Ludusavi manifest, dedupe |
| ↳ Backup / compress | ~1,920 | Copy saves, 7-Zip, retention, manifests |
| ↳ Other core | ~600 | Paths, models, helpers |
| **GSBT.Core.Tests** | ~910 | Unit tests (engine only) |
| **GSBT.WinUI** (all UI) | **~20,900 C#** + **~1,800 XAML** | Main window + sandbox + charts |
| **Whole repo (C#)** | **~26,200** | |

### Main app vs sandbox (WinUI)

Sandbox is **not** a separate product — it is optional developer/diagnostic UI in the same WinUI project.

| Component | Approx. lines | Notes |
|-----------|---------------|--------|
| **Main window (`MainPage` + XAML)** | **~5,100** | Split across several `.cs` partial files |
| **MainViewModel** | **~2,500** | Partials: Scan, Backup, Catalog, Settings, Integrity |
| **Settings UI** | **~1,460** | Mostly code-built UI |
| **Game table** | **~1,270** | List/grid controls |
| **App shell** (tray, notifications, theme) | **~3,400** | |
| **Sandbox monitor bundle** | **~7,700** | Monitor window, benchmarks, charts, simulation |

**Main app UI (rough):** ~15,000 lines (WinUI minus sandbox-specific bundle).

Largest single areas:

- `MainPage` partials combined: **~4,440 C#**
- `SettingsPanel.cs`: **~1,460**
- `PerformanceChartDetailDialog.cs`: **~1,270**

---

## What is done well

1. **Engine vs UI split** — `GSBT.Core` holds real logic; WinUI wires UI. Core has unit tests.
2. **Partial classes by concern** — `MainViewModel` and `MainPage` split into Scan, Backup, Catalog, etc.
3. **Installer + publish hygiene** — validate publish output, sandbox hard links, PRI hard links.
4. **Documentation** — README, PRIVACY, CHANGELOG, sandbox/installer notes.
5. **Single shipping binary** — main and sandbox modes share one executable.
6. **Sensible defaults** — empty game list until Scan; notifications off by default.

---

## What is rough (fine for v0.1)

1. **`MainPage` is still large** (~5k lines) even with partials.
2. **`SettingsPanel.cs` ~1,460 lines** — hard to skim for newcomers.
3. **`PerformanceChartDetailDialog` ~1,270 lines** — sandbox/diagnostic heavy.
4. **No WinUI automated tests** — manual QA only.
5. **Icon patterns mixed** — footer chrome centralized; sandbox/diagnostic UI still uses inline glyphs.
6. **Unsigned installer** — SmartScreen warning is expected for a small OSS project.

This is **large and uneven**, not unmaintainable spaghetti. Boundaries (Core / ViewModel / Views / Services) are real.

---

## Icons — standardized or messy?

**Partially standardized.**

| Done well | Still ad hoc |
|-----------|--------------|
| `AppBrandingIcons` — window, taskbar, toast icons | Sandbox views (inline `FontIcon` + glyph strings) |
| `MainPage.CommandBar` — shared footer glyph helpers | `BatchTestCardBuilder`, `BenchmarkResultCardBuilder` |
| | Parts of `SettingsPanel`, `MainPage.xaml` |

A future **low-risk** cleanup: one helper like `GsbtFluentIcon.Create(glyph, size)` and migrate inline icons.

---

## Refactoring feasibility

| Refactor | Difficulty | Risk | Worth it now? |
|----------|------------|------|----------------|
| Unify icon helpers | Medium | Low | Nice, not urgent |
| Split `SettingsPanel` into tabs/files | Medium | Low–medium | Only if settings keep growing |
| Shrink `MainPage` further | High | Medium | Only with time or UI tests |
| Split sandbox into another repo/project | High | High | **No** — shared Core and exe |
| Extract compression UI from settings | Medium | Medium | Later |

Risky moves: merging ViewModels or changing `MainViewModel` APIs used from many `MainPage` partials.

---

## Local folder rename

If the repo folder is still named like a backup copy, close Cursor and run from an external terminal:

```bat
cd /d d:\Projects\GitHub
rename "GSBT_C# - Backup (before exe) - Copy" "Game Save Backup Tool"
```

Then reopen `Game Save Backup Tool` in Cursor.

---

## Dev docs

| Document | Purpose |
|----------|---------|
| [dev/RELEASE_CHECKLIST.md](dev/RELEASE_CHECKLIST.md) | Pre-release engineering tasks |
| [dev/CursorAgentGuide.md](dev/CursorAgentGuide.md) | WinUI UI conventions for agents/contributors |
| [../src-winui/docs/SANDBOX.md](../src-winui/docs/SANDBOX.md) | Optional `-s` sandbox mode |
| [../src-winui/docs/INSTALLER_PLAN.md](../src-winui/docs/INSTALLER_PLAN.md) | Installer layout notes |

---

*Last updated: June 2026.*
