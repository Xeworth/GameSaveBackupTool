# GSBT WinUI — guidance for Cursor sessions

Point the AI to this file when starting work on the WinUI edition so preferences stay consistent across chats.

## Toast notifications (`ShowStatusToastAsync` / status strip)

The existing bottom-centre status strip toasts are intentional UX: short-lived, animated, non-blocking. **Treat them as the default way** to confirm actions, report failures, or give quick feedback.

**Agent discretion:** Unless the user asks otherwise, **add or reuse these toasts where appropriate** (successful picks, validation failures, cancelled operations, lightweight confirmations). Do not wait for the user to spell out every toast.

Do **not** replace or redesign OS-level notifications (`OsAppNotifications`, tray, etc.) unless the task explicitly asks for it.

## Integrity / warning messaging (in-app, dismissible)

Longer-lived warnings (e.g. backup folder missing, integrity issues) stay **inside the main window** — **not** as a substitute for the short status toasts users already expect.

Style them **like the existing status strip** (`StatusToastBorder` in [`src-winui/src/GSBT.WinUI/Views/MainPage.xaml`](../../src-winui/src/GSBT.WinUI/Views/MainPage.xaml)): same theme brushes (`GsbtHeaderStripBrush`, `GsbtBorderBrush`, `GsbtBodyTextBrush`), ~12px text, corner radius, bottom-centre placement. Extend that pattern with a clear dismiss affordance: **close (“X”)** and/or **OK / Confirm**, picking whichever WinUI Gallery pattern fits **without** breaking this chrome.

Avoid a second, unrelated visual language for “almost the same thing” as the floating status message.

## Toast duration (optional product follow-up)

Consider **slightly longer defaults** when tuning UX. Prefer a **Settings** control that picks duration from a **short list of intervals** (e.g. **1–5 seconds** in 1 second steps), persisted in `SettingsStore` and read by `ShowStatusToastAsync` / `ShowStatusToastCoreAsync`. Until then, keep current millisecond defaults in code.

## Sandbox (`-s`) and installers

- **Never** package sandbox as monitor-only or disconnected from the main `MainPage` shell. `-s` must open **Main +** `SandboxMonitorWindow`; theme via `ThemeBridge`.
- Optional sandbox installer = shortcut to same `gsbt.exe -s` after Main is installed; see [INSTALLER_PLAN.md](../../src-winui/docs/INSTALLER_PLAN.md).
- When started with `-s`, apply `branding/gsbt-s.ico` to the **main** window as well (taskbar/icon QA).

## `MainViewModel` layout (WinUI orchestration vs `GSBT.Core`)

`MainViewModel` is a **partial class** under [`src-winui/src/GSBT.WinUI/ViewModels/`](../../src-winui/src/GSBT.WinUI/ViewModels/):

| File | Responsibility |
|------|----------------|
| `MainViewModel.cs` | ctor, grid state, footer/progress, teaching tips, toasts |
| `MainViewModel.Scan.cs` | manifest refresh, game detection, save-path fetch |
| `MainViewModel.Catalog.cs` | filter, selection, add/rename rows, persisted catalog |
| `MainViewModel.Backup.cs` | manual backup, compress, estimates, 7-Zip install |
| `MainViewModel.Integrity.cs` | last-backup disk/checkpoint warnings |
| `MainViewModel.Settings.cs` | load/save settings payload, window size prefs |
| `MainViewModel.Types.cs` | `BackupGamesOutcome`, `MainSettingsPayload`, toast enums |

**Reusing scan/backup/compress from Python editions:** put shared logic in **`GSBT.Core`** (`ScanService`, `SaveFolderBackupService`, `RegistrySaveBackupService`, `BackupCompressionService`, etc.). The view model only wires settings, UI thread, and `GameRowViewModel` updates.

## Icons

- App/window/tray/toast branding: [`AppBrandingIcons.cs`](../../src-winui/src/GSBT.WinUI/Services/AppBrandingIcons.cs)
- Footer command bar glyphs: [`MainPage.CommandBar.cs`](../../src-winui/src/GSBT.WinUI/Views/MainPage.CommandBar.cs) (`FooterGlyphs`, `CreateIconLabelRow`)
- Prefer extending those helpers before adding new inline `FontIcon` blocks in sandbox/diagnostic views.

## Release engineering

See [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) for smoke tests and open installer tasks.
