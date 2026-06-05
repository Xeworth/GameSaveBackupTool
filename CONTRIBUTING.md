# Contributing to GSBT

Thank you for your interest in **Game Save Backup Tool**. Contributions are welcome via GitHub issues and pull requests.

## Getting started

1. Clone the repository
2. For the **WinUI (Native)** edition: install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows 10/11
3. Build and run:

   ```bat
   cd src-winui
   launch.bat
   ```

4. Run tests:

   ```bat
   dotnet test src-winui\GSBT.sln -c Debug
   ```

See [README.md](README.md) for monorepo layout. WinUI build, publish, and installer steps: [src-winui/README.md](src-winui/README.md).

Agent / contributor UX conventions: [docs/winui/dev/CursorAgentGuide.md](docs/winui/dev/CursorAgentGuide.md).

## Pull requests

- Keep changes focused; match existing naming and patterns
- Run `dotnet build` and `dotnet test` before opening a PR (from `src-winui/`)
- Update [CHANGELOG.md](CHANGELOG.md) under **Unreleased** for user-visible changes
- Do not commit secrets, personal `game_save_data.json`, or `%AppData%` dumps

## Pre-release work

Engineering tasks are tracked in **[docs/winui/dev/RELEASE_CHECKLIST.md](docs/winui/dev/RELEASE_CHECKLIST.md)**.

## Sandbox / dev mode

Optional developer UI is documented in **[src-winui/docs/SANDBOX.md](src-winui/docs/SANDBOX.md)**. Normal users should not need `-s`.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
