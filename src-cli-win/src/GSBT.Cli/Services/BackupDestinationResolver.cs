using GSBT.Cli.Output;
using GSBT.Cli.Settings;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Services;

public static class BackupDestinationResolver
{
    public sealed record Request(
        string? ExplicitPath,
        bool SetDefault,
        bool AcceptSuggestion,
        bool NonInteractive = false);

    public static bool TryResolve(WinUiSettingsStore store, Request request, out string path, out bool savedAsDefault)
    {
        savedAsDefault = false;
        path = string.Empty;

        if (!string.IsNullOrWhiteSpace(request.ExplicitPath))
        {
            if (!BackupDestinationPolicy.TryNormalizePath(request.ExplicitPath, out path, out var err))
            {
                if (!request.NonInteractive)
                {
                    CliConsoleFormatter.WriteError(err ?? "Invalid path.");
                }

                return false;
            }

            if (request.SetDefault)
            {
                store.Set("default_backup_path", path);
                savedAsDefault = true;
            }

            return true;
        }

        if (BackupDestinationPolicy.HasPersistedDefault(store.ContainsKey, store.Get))
        {
            return BackupDestinationPolicy.TryNormalizePath(
                store.Get("default_backup_path", string.Empty),
                out path,
                out _);
        }

        var acceptSuggestion = request.AcceptSuggestion || request.NonInteractive;

        if (acceptSuggestion)
        {
            if (BackupDestinationPolicy.TryResolveNonInteractive(
                    null,
                    acceptSuggestion: true,
                    store.ContainsKey,
                    store.Get,
                    out path,
                    out var err))
            {
                if (request.SetDefault)
                {
                    store.Set("default_backup_path", path);
                    savedAsDefault = true;
                }

                return true;
            }

            if (!request.NonInteractive && !string.IsNullOrWhiteSpace(err))
            {
                CliConsoleFormatter.WriteError(err);
            }

            return false;
        }

        if (request.NonInteractive)
        {
            return false;
        }

        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            return TryResolveInteractive(store, out path, out savedAsDefault);
        }

        CliConsoleFormatter.WriteError(
            "No backup destination configured. Use --path <dir>, --yes to accept the suggested path, "
            + "or run gsbt settings backup-path <dir>.");
        return false;
    }

    private static bool TryResolveInteractive(WinUiSettingsStore store, out string path, out bool savedAsDefault)
    {
        savedAsDefault = false;
        path = string.Empty;
        var suggestion = store.GetBackupPathSuggestion();

        AnsiConsole.MarkupLine("[bold]Backup destination[/]");
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            AnsiConsole.MarkupLine($"  Suggested: [cyan]{Markup.Escape(suggestion)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [yellow]No default folder set yet — choose one now.[/]");
        }

        var prompt = new TextPrompt<string>("  Folder path")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            prompt.DefaultValue(suggestion);
        }

        var chosen = AnsiConsole.Prompt(prompt).Trim();
        if (string.IsNullOrWhiteSpace(chosen) && !string.IsNullOrWhiteSpace(suggestion))
        {
            chosen = suggestion;
        }

        if (!BackupDestinationPolicy.TryNormalizePath(chosen, out path, out var err))
        {
            CliConsoleFormatter.WriteError(err ?? "Invalid path.");
            return false;
        }

        if (!store.HasPersistedDefaultBackupPath())
        {
            if (AnsiConsole.Confirm("  Save as default backup folder?", defaultValue: true))
            {
                store.Set("default_backup_path", path);
                savedAsDefault = true;
            }
        }

        return true;
    }

    public static void RecordLastBackupPath(WinUiSettingsStore store, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        store.Set("last_backup_path", path);
    }
}
