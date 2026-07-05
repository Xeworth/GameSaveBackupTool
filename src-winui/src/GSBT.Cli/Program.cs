using System.CommandLine;
using System.CommandLine.Completions;
using System.Diagnostics;
using GSBT.Cli.Commands;
using GSBT.Cli.Completion;
using GSBT.Cli.Output;

namespace GSBT.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // best-effort UTF-8 for branch glyphs (● ┌ ├)
        }

        if (CliHelpRouter.TryDispatch(args, out var helpExit))
        {
            return helpExit;
        }

        var host = new CliHost();
        var jsonOption = new Option<bool>("--json") { Description = "Machine-readable JSON output" };
        var aiOption = new Option<bool>("--ai")
        {
            Description = "Agent mode: JSON + no progress UI + no interactive prompts (implies --json where supported)",
        };

        var refreshOption = new Option<bool>("--refresh-manifest")
        {
            Description = "Download latest Ludusavi manifest before scanning",
        };

        var targetsArgument = new Argument<string[]>("targets")
        {
            Description = "Row numbers (2, 2-5), or game names (fuzzy)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        targetsArgument.CompletionSources.Add(ctx =>
            CatalogNameCompletion.GetCompletions(host, ctx.WordToComplete ?? string.Empty));

        var scanCommand = new Command("scan", "Detect installed games and resolve save paths");
        scanCommand.Options.Add(refreshOption);
        scanCommand.Options.Add(aiOption);
        scanCommand.SetAction(async (parse, ct) =>
        {
            var refresh = parse.GetValue(refreshOption);
            var mode = CliOutputMode.From(json: false, ai: parse.GetValue(aiOption));
            return await ScanCommand.RunAsync(host, refresh, mode).ConfigureAwait(false);
        });

        var listFilterArgument = new Argument<string?>("filter")
        {
            Description = "found (default), not-found, or all",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var listCommand = new Command("list", "Show numbered game catalog");
        listCommand.Options.Add(jsonOption);
        listCommand.Options.Add(aiOption);
        listCommand.Arguments.Add(listFilterArgument);
        listCommand.SetAction((parse, ct) =>
        {
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            var filter = parse.GetValue(listFilterArgument);
            return Task.FromResult(ListCommand.Run(host, filter, mode));
        });

        var backupPathOption = new Option<string?>("--path")
        {
            Description = "Backup destination folder (overrides default)",
        };
        var backupSetDefaultOption = new Option<bool>("--set-default")
        {
            Description = "Save --path as the default backup folder",
        };
        var backupYesOption = new Option<bool>("--yes")
        {
            Description = "Accept suggested backup path without prompting",
        };

        var backupCommand = new Command("backup", "Backup game saves");
        backupCommand.Options.Add(jsonOption);
        backupCommand.Options.Add(aiOption);
        backupCommand.Options.Add(backupPathOption);
        backupCommand.Options.Add(backupSetDefaultOption);
        backupCommand.Options.Add(backupYesOption);
        backupCommand.Arguments.Add(targetsArgument);
        backupCommand.SetAction((parse, ct) =>
        {
            using var cancel = new CliCancelSource(ct);
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            var targets = parse.GetValue(targetsArgument) ?? [];
            var path = parse.GetValue(backupPathOption);
            var setDefault = parse.GetValue(backupSetDefaultOption);
            var yes = parse.GetValue(backupYesOption);
            return Task.FromResult(BackupCommand.Run(
                host, targets, mode, path, setDefault, yes, cancel.Token));
        });

        var compressTargetsArgument = new Argument<string[]>("targets")
        {
            Description = "Row numbers (2, 2-5), or game names (fuzzy)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        compressTargetsArgument.CompletionSources.Add(ctx =>
            CatalogNameCompletion.GetCompletions(host, ctx.WordToComplete ?? string.Empty));

        var compressCommand = new Command("compress", "Compress backup folder to .7z");
        compressCommand.Options.Add(jsonOption);
        compressCommand.Options.Add(aiOption);
        compressCommand.Arguments.Add(compressTargetsArgument);
        compressCommand.SetAction(async (parse, ct) =>
        {
            using var cancel = new CliCancelSource(ct);
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            var targets = parse.GetValue(compressTargetsArgument) ?? [];
            return await CompressCommand.RunAsync(host, targets, mode, cancel.Token).ConfigureAwait(false);
        });

        var settingsArgsArgument = new Argument<string[]>("args")
        {
            Description = "show | backup-path [path] | compression [show|explain|set ...]",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var settingsCommand = new Command("settings", "View or change GSBT settings");
        settingsCommand.Arguments.Add(settingsArgsArgument);
        settingsCommand.SetAction((parse, ct) =>
        {
            var settingsArgs = parse.GetValue(settingsArgsArgument) ?? [];
            return Task.FromResult(SettingsCommand.Run(host, settingsArgs));
        });

        var customGameArgument = new Argument<string>("gameName")
        {
            Description = "Display name for the custom game",
        };
        var customFolderArgument = new Argument<string>("saveFolder")
        {
            Description = "Path to the game's save folder",
        };

        var addCustomCommand = new Command("custom", "Add a custom game with a save folder");
        addCustomCommand.Arguments.Add(customGameArgument);
        addCustomCommand.Arguments.Add(customFolderArgument);
        addCustomCommand.SetAction((parse, ct) =>
        {
            var name = parse.GetValue(customGameArgument) ?? string.Empty;
            var folder = parse.GetValue(customFolderArgument) ?? string.Empty;
            return Task.FromResult(AddCustomCommand.Run(host, name, folder));
        });

        var addCommand = new Command("add", "Add catalog entries");
        addCommand.Subcommands.Add(addCustomCommand);

        var statusCommand = new Command("status", "Show backup path and settings summary");
        statusCommand.Options.Add(jsonOption);
        statusCommand.Options.Add(aiOption);
        statusCommand.SetAction((parse, ct) =>
        {
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            if (mode.Json)
            {
                CliConsoleFormatter.WriteStatusJson(host, mode.Ai);
            }
            else
            {
                CliConsoleFormatter.WriteCommandStart("gsbt status");
                CliConsoleFormatter.WriteStatus(host);
                CliConsoleFormatter.WriteCommandEnd();
            }

            return Task.FromResult(0);
        });

        var helpTopicArgument = new Argument<string?>("command")
        {
            Description = "Command name for detailed help (e.g. backup, compress)",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var helpCommand = new Command("help", "Command reference and guides");
        helpCommand.Options.Add(aiOption);
        helpCommand.Arguments.Add(helpTopicArgument);
        helpCommand.SetAction((parse, ct) =>
        {
            if (parse.GetValue(aiOption))
            {
                CliAiContract.WriteCapabilities();
                return Task.FromResult(0);
            }

            var topic = parse.GetValue(helpTopicArgument);
            if (string.IsNullOrWhiteSpace(topic))
            {
                CliHelpContent.WriteHub();
            }
            else if (!CliHelpContent.TryWriteCommandHelp(topic))
            {
                CliConsoleFormatter.WriteError($"Unknown help topic \"{topic}\". Run gsbt help for a list.");
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        });

        var guiCommand = new Command("gui", "Open the WinUI desktop app");
        guiCommand.SetAction((_, ct) => Task.FromResult(LaunchGuiApp()));

        var getForceOption = new Option<bool>("--force")
        {
            Description = "Re-download and run the GUI installer even if gsbt-main.exe exists",
        };

        var getGuiCommand = new Command("gui", "Download and silently install the WinUI GUI from GitHub");
        getGuiCommand.Options.Add(aiOption);
        getGuiCommand.Options.Add(getForceOption);
        getGuiCommand.SetAction(async (parse, ct) =>
        {
            var mode = CliOutputMode.From(json: false, ai: parse.GetValue(aiOption));
            var force = parse.GetValue(getForceOption);
            return await GetGuiCommand.RunAsync(mode, force, ct).ConfigureAwait(false);
        });

        var getCommand = new Command("get", "Download GSBT components from GitHub");
        getCommand.Subcommands.Add(getGuiCommand);

        var root = new RootCommand("Game Save Backup Tool — backup PC game saves from the terminal");
        root.Subcommands.Add(scanCommand);
        root.Subcommands.Add(listCommand);
        root.Subcommands.Add(backupCommand);
        root.Subcommands.Add(compressCommand);
        root.Subcommands.Add(settingsCommand);
        root.Subcommands.Add(addCommand);
        root.Subcommands.Add(statusCommand);
        root.Subcommands.Add(getCommand);
        root.Subcommands.Add(guiCommand);
        root.Subcommands.Add(helpCommand);
        root.SetAction((_, ct) =>
        {
            CliHelpContent.WriteMainMenu();
            return Task.FromResult(0);
        });

        try
        {
            var parseResult = root.Parse(args);
            return await parseResult.InvokeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (args.Any(a => string.Equals(a, "--ai", StringComparison.OrdinalIgnoreCase)))
            {
                CliAiContract.WriteError(ResolveCommandName(args), ex.Message, 2, "unhandled_exception");
            }
            else
            {
                CliConsoleFormatter.WriteError(ex.Message);
            }

            return 2;
        }
    }

    private static string ResolveCommandName(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return arg;
            }
        }

        return "root";
    }

    private static int LaunchGuiApp()
    {
        CliConsoleFormatter.WriteCommandStart("gsbt gui");
        var gui = Path.Combine(AppContext.BaseDirectory, "gsbt-main.exe");
        if (!File.Exists(gui))
        {
            CliConsoleFormatter.WriteError(
                "GUI is not installed beside gsbt.exe. Run gsbt get gui to download the WinUI installer, " +
                "or install the full GSBT package.");
            CliConsoleFormatter.WriteCommandEnd();
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(gui)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            CliConsoleFormatter.WriteCommandEnd();
            return 0;
        }
        catch (Exception ex)
        {
            CliConsoleFormatter.WriteError($"Could not start GUI: {ex.Message}");
            CliConsoleFormatter.WriteCommandEnd();
            return 2;
        }
    }
}
