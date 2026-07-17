# .NET 10 Migration Notes

## Completed in this migration

- Active .NET projects now target `net10.0-windows10.0.19041.0` through `Directory.Build.props`.
- Core, CLI, WinUI, test, and screen saver packer dependencies were updated for the .NET 10 pass.
- Release, launcher, portable, installer, and validation paths were moved to the .NET 10 output folder.
- Release scripts prefer `%USERPROFILE%\.dotnet10\dotnet.exe` when present, then fall back to `dotnet` on `PATH`.
- Sandbox release behavior is intentionally preserved: `gsbt-sandbox.exe` is copied from `gsbt-main.exe`, gets the sandbox icon through `rcedit`, and uses a `gsbt-sandbox.pri` alias.
- WinUI view models now use CommunityToolkit.Mvvm partial properties for observable state, removing the WinRT/AOT `MVVMTK0045` warnings.
- Public executable and branding names are centralized in `AppIdentity`.
- Batch scripts share target framework and local SDK fallback setup through `scripts\_env.bat`.

## Follow-up cleanup candidates

- Some existing docs/comments may still contain UTF-8 punctuation that renders as mojibake in legacy shells. Prefer ASCII in new script/docs text unless a file intentionally uses UTF-8 prose.
- Icon handling still has several valid code paths: app manifest icon, shortcut icons, `AppWindow.SetIcon`, sandbox executable icon patching, and toast icon copies. `AppIdentity` now centralizes names, but deeper icon QA should stay part of release testing.
- If future release tooling adds another target runtime or architecture, extend `scripts\_env.bat` first so helper scripts stay consistent.
