using Spectre.Console;

namespace GSBT.Cli.Output;

public static class CliCommandSuggester
{
    private static readonly string[] AlwaysAvailableRootCommands =
    [
        "scan",
        "list",
        "backup",
        "compress",
        "verify",
        "restore",
        "settings",
        "add",
        "status",
        "diagnostics",
        "help",
    ];

    public static bool TryWriteRootSuggestion(IReadOnlyList<string> args, bool ai)
    {
        var rootCommands = AlwaysAvailableRootCommands
            .Append(CliInstallationState.IsGuiInstalled ? "gui" : "get")
            .ToArray();
        var first = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(first) || rootCommands.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var suggestion = first.Equals("getgui", StringComparison.OrdinalIgnoreCase)
            ? CliInstallationState.IsGuiInstalled ? "gsbt gui" : "gsbt get gui"
            : CliInstallationState.IsGuiInstalled && first.Equals("get", StringComparison.OrdinalIgnoreCase)
                ? "gsbt gui"
                : !CliInstallationState.IsGuiInstalled && first.Equals("gui", StringComparison.OrdinalIgnoreCase)
                    ? "gsbt get gui"
                    : Closest(first, rootCommands) is { } closest
                ? $"gsbt {closest}"
                : null;

        if (suggestion is null)
        {
            return false;
        }

        var message = $"Unknown command {first}. Did you mean \"{suggestion}\"?";
        if (ai)
        {
            CliAiContract.WriteError("root", message, 1, "unknown_command");
            return true;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown command [bold]{Markup.Escape(first)}[/]. Did you mean \"[bold]{Markup.Escape(suggestion)}[/]\"?");
        AnsiConsole.WriteLine();
        return true;
    }

    public static string? SuggestSettingsCommand(string value)
    {
        var commands = new[] { "show", "backup-path", "compression" };
        if (value.Equals("backuppath", StringComparison.OrdinalIgnoreCase)
            || value.Equals("backup_path", StringComparison.OrdinalIgnoreCase))
        {
            return "backup-path";
        }

        return Closest(value, commands);
    }

    public static string? SuggestCompressionCommand(string value)
    {
        var commands = new[] { "show", "explain", "set" };
        return Closest(value, commands);
    }

    public static string? SuggestCompressionSetting(string value)
    {
        var commands = new[] { "level", "threads", "mode" };
        return Closest(value, commands);
    }

    private static string? Closest(string value, IReadOnlyList<string> candidates)
    {
        var best = candidates
            .Select(c => new { Candidate = c, Distance = Distance(value.ToLowerInvariant(), c.ToLowerInvariant()) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
        return best is not null && best.Distance <= Math.Max(2, value.Length / 3)
            ? best.Candidate
            : null;
    }

    private static int Distance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            var previous = costs[0];
            costs[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var temp = costs[j];
                costs[j] = Math.Min(
                    Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    previous + (left[i - 1] == right[j - 1] ? 0 : 1));
                previous = temp;
            }
        }

        return costs[right.Length];
    }
}
