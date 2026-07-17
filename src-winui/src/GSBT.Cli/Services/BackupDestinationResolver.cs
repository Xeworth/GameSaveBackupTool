using GSBT.Cli.Output;
using GSBT.Cli.Input;
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

    public static bool TryResolve(
        WinUiSettingsStore store,
        Request request,
        out string path,
        out bool savedAsDefault,
        out bool canceled)
    {
        savedAsDefault = false;
        canceled = false;
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
            return TryResolveInteractive(store, out path, out savedAsDefault, out canceled);
        }

        CliConsoleFormatter.WriteError(
            "No backup destination configured. Use --path <dir>, --yes to accept the suggested path, "
            + "or run gsbt settings backup-path <dir>.");
        return false;
    }

    private static bool TryResolveInteractive(
        WinUiSettingsStore store,
        out string path,
        out bool savedAsDefault,
        out bool canceled)
    {
        savedAsDefault = false;
        canceled = false;
        path = string.Empty;
        var suggestion = store.GetBackupPathSuggestion();

        AnsiConsole.MarkupLine("[bold]Backup destination[/]");
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            AnsiConsole.MarkupLine($"  Suggested path: [cyan]{Markup.Escape(suggestion)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [yellow]No default folder set yet - choose one now.[/]");
        }

        var chosen = ChooseBackupPath(suggestion);
        if (chosen.Canceled)
        {
            canceled = true;
            return false;
        }

        if (!BackupDestinationPolicy.TryNormalizePath(chosen.Path, out path, out var err))
        {
            CliConsoleFormatter.WriteError(err ?? "Invalid path.");
            return false;
        }

        if (!store.HasPersistedDefaultBackupPath())
        {
            var saveDefault = CliPrompt.Confirm("  Save as default backup folder?", defaultValue: true);
            if (saveDefault == CliConfirmation.Canceled)
            {
                canceled = true;
                return false;
            }

            if (saveDefault == CliConfirmation.Accepted)
            {
                store.Set("default_backup_path", path);
                savedAsDefault = true;
            }
        }

        return true;
    }

    private enum BackupPathChoice
    {
        UseSuggestion,
        SelectPath,
        Cancel,
    }

    private readonly record struct BackupPathResult(string Path, bool Canceled = false);

    private static readonly CliChoice<BackupPathChoice>[] BackupPathChoices =
    [
        new(BackupPathChoice.UseSuggestion, "use suggested path", "1", "u", "use", "suggest", "suggested", "suggestion", "suggested path", "use suggestion", "use suggested", "default", "use default"),
        new(BackupPathChoice.SelectPath, "select path", "2", "select", "select folder", "choose", "choose path", "choose folder", "browse", "custom", "custom path", "another path"),
        new(BackupPathChoice.Cancel, "cancel", "3", "c", "back", "exit", "quit", "stop", "never mind", "nevermind"),
    ];

    private static BackupPathResult ChooseBackupPath(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return PromptForFolderPath();
        }

        AnsiConsole.MarkupLine("  [green][[1]] Use suggested path[/] [dim](default)[/]");
        AnsiConsole.MarkupLine("  [[2]] Select another path");
        AnsiConsole.MarkupLine("  [[3]] Cancel");
        AnsiConsole.MarkupLine("  [dim]Enter accepts the default. Words and short forms work too. Esc cancels.[/]");
        while (true)
        {
            var answer = CliPrompt.ReadLine("  Choice: ");
            if (answer.IsCanceled)
            {
                return new BackupPathResult(string.Empty, Canceled: true);
            }

            if (string.IsNullOrWhiteSpace(answer.Value))
            {
                return new BackupPathResult(suggestion);
            }

            if (CliPrompt.TryMatch(answer.Value, BackupPathChoices, out var choice, out var suggestedChoice))
            {
                return choice switch
                {
                    BackupPathChoice.UseSuggestion => new BackupPathResult(suggestion),
                    BackupPathChoice.SelectPath => PromptForFolderPath(),
                    _ => new BackupPathResult(string.Empty, Canceled: true),
                };
            }

            if (LooksLikePath(answer.Value))
            {
                return new BackupPathResult(Unquote(answer.Value));
            }

            CliConsoleFormatter.WriteWarning(suggestedChoice is null
                ? "I did not recognize that. Try 1/suggested, 2/select, 3/cancel, or enter a folder path."
                : $"I did not recognize that. Did you mean '{suggestedChoice}'?");
        }
    }

    private static BackupPathResult PromptForFolderPath()
    {
        AnsiConsole.MarkupLine("  [dim]Type or paste a folder path. Type 'cancel' or press Esc to cancel.[/]");
        while (true)
        {
            var answer = CliPrompt.ReadLine("  Folder path: ");
            if (answer.IsCanceled || CliPrompt.IsCancellationText(answer.Value))
            {
                return new BackupPathResult(string.Empty, Canceled: true);
            }

            if (!string.IsNullOrWhiteSpace(answer.Value))
            {
                return new BackupPathResult(Unquote(answer.Value));
            }

            CliConsoleFormatter.WriteWarning("Enter a folder path, or press Esc to cancel.");
        }
    }

    private static bool LooksLikePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Path.IsPathRooted(value)
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.StartsWith("~", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('/');
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1].Trim()
            : trimmed;
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
