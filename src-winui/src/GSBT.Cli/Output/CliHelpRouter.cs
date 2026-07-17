namespace GSBT.Cli.Output;

public static class CliHelpRouter
{
    private static readonly HashSet<string> AlwaysAvailableCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "scan", "list", "backup", "compress", "verify", "restore", "add", "settings", "status",
        "diagnostics", "help",
    };

    /// <summary>Handles <c>gsbt help [cmd]</c> and <c>gsbt &lt;cmd&gt; --help</c> before command invocation.</summary>
    public static bool TryDispatch(IReadOnlyList<string> args, out int exitCode)
    {
        exitCode = 0;
        if (args.Count == 0)
        {
            return false;
        }

        if (args.Any(a => a.Equals("--ai", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count == 1)
            {
                CliHelpContent.WriteHub();
            }
            else
            {
                var topic = args[1].Equals("custom", StringComparison.OrdinalIgnoreCase) && args.Count > 2
                    ? "add"
                    : args[1];
                if (!CliHelpContent.TryWriteCommandHelp(topic))
                {
                    CliConsoleFormatter.WriteError($"Unknown help topic \"{args[1]}\". Run gsbt help for a list.");
                    exitCode = 1;
                }
            }

            return true;
        }

        if (!IsHelpToken(args[^1]))
        {
            return false;
        }

        var cmd = args[0];
        if (cmd.Equals("add", StringComparison.OrdinalIgnoreCase)
            && args.Count >= 3
            && args[1].Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            cmd = "add";
        }
        else if (cmd.Equals("get", StringComparison.OrdinalIgnoreCase)
            && args.Count >= 3
            && args[1].Equals("gui", StringComparison.OrdinalIgnoreCase))
        {
            cmd = "get";
        }

        if (!IsAvailableCommand(cmd) || cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!CliHelpContent.TryWriteCommandHelp(cmd))
        {
            return false;
        }

        return true;
    }

    private static bool IsHelpToken(string token) =>
        token.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-?", StringComparison.OrdinalIgnoreCase);

    private static bool IsAvailableCommand(string command) =>
        AlwaysAvailableCommands.Contains(command)
        || (CliInstallationState.IsGuiInstalled
            ? command.Equals("gui", StringComparison.OrdinalIgnoreCase)
            : command.Equals("get", StringComparison.OrdinalIgnoreCase));
}
