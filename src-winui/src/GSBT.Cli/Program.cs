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
        var guiInstalled = CliInstallationState.IsGuiInstalled;
        var jsonOption = new Option<bool>("--json") { Description = "Machine-readable JSON output" };
        var aiOption = new Option<bool>("--ai")
        {
            Description = "Agent mode: JSON result + progress events on stderr + no interactive prompts",
        };

        var refreshOption = new Option<bool>("--refresh-manifest")
        {
            Description = "Download latest Ludusavi manifest before scanning",
        };
        var fullScanOption = new Option<bool>("--full")
        {
            Description = "Recheck every detected title, including previously not-found games",
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
        scanCommand.Options.Add(fullScanOption);
        scanCommand.Options.Add(aiOption);
        scanCommand.SetAction(async (parse, ct) =>
        {
            var refresh = parse.GetValue(refreshOption);
            var full = parse.GetValue(fullScanOption);
            var mode = CliOutputMode.From(json: false, ai: parse.GetValue(aiOption));
            return await ScanCommand.RunAsync(host, refresh, full, mode).ConfigureAwait(false);
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
        settingsCommand.Options.Add(jsonOption);
        settingsCommand.Options.Add(aiOption);
        settingsCommand.Arguments.Add(settingsArgsArgument);
        settingsCommand.SetAction((parse, ct) =>
        {
            var settingsArgs = parse.GetValue(settingsArgsArgument) ?? [];
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            return Task.FromResult(SettingsCommand.Run(host, settingsArgs, mode));
        });

        var customGameArgument = new Argument<string>("gameName")
        {
            Description = "Display name for the custom backup entry",
        };
        var customFolderArgument = new Argument<string>("saveFolder")
        {
            Description = "Path to the folder that should be backed up",
        };

        var addCustomCommand = new Command("custom", "Add a custom folder backup entry");
        addCustomCommand.Options.Add(jsonOption);
        addCustomCommand.Options.Add(aiOption);
        addCustomCommand.Arguments.Add(customGameArgument);
        addCustomCommand.Arguments.Add(customFolderArgument);
        addCustomCommand.SetAction((parse, ct) =>
        {
            var name = parse.GetValue(customGameArgument) ?? string.Empty;
            var folder = parse.GetValue(customFolderArgument) ?? string.Empty;
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            return Task.FromResult(AddCustomCommand.Run(host, name, folder, mode));
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

        var verifyTargetsArgument = new Argument<string[]>("targets")
        {
            Description = "Backed-up row numbers or game names",
            Arity = ArgumentArity.ZeroOrMore,
        };
        verifyTargetsArgument.CompletionSources.Add(ctx =>
            CatalogNameCompletion.GetCompletions(host, ctx.WordToComplete ?? string.Empty));
        var verifyFullOption = new Option<bool>("--full")
        {
            Description = "Verify SHA-256 content hashes in addition to file inventory and size",
        };
        var verifyCommand = new Command("verify", "Verify retained backup snapshots");
        verifyCommand.Options.Add(jsonOption);
        verifyCommand.Options.Add(aiOption);
        verifyCommand.Options.Add(verifyFullOption);
        verifyCommand.Arguments.Add(verifyTargetsArgument);
        verifyCommand.SetAction((parse, ct) =>
        {
            using var cancel = new CliCancelSource(ct);
            var mode = CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption));
            var targets = parse.GetValue(verifyTargetsArgument) ?? [];
            var verificationMode = parse.GetValue(verifyFullOption)
                ? GSBT.Core.Models.BackupVerificationMode.Full
                : GSBT.Core.Models.BackupVerificationMode.Fast;
            return Task.FromResult(VerifyCommand.Run(host, targets, verificationMode, mode, cancel.Token));
        });

        var restoreTargetArgument = new Argument<string[]>("game")
        {
            Description = "One backed-up row number or game name",
            Arity = ArgumentArity.OneOrMore,
        };
        restoreTargetArgument.CompletionSources.Add(ctx =>
            CatalogNameCompletion.GetCompletions(host, ctx.WordToComplete ?? string.Empty));
        var restoreSnapshotOption = new Option<string>("--snapshot")
        {
            Description = "Snapshot selector: latest (default), run ID, timestamp text, or retained path",
            DefaultValueFactory = _ => "latest",
        };
        var restoreToOption = new Option<string?>("--to")
        {
            Description = "Restore to an alternate folder instead of the live save path",
        };
        var restoreModeOption = new Option<string>("--mode")
        {
            Description = "replace (default) or merge",
            DefaultValueFactory = _ => "replace",
        };
        var restoreDryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Validate and preview without changing files or registry data",
        };
        var restoreYesOption = new Option<bool>("--yes")
        {
            Description = "Confirm the restore without an interactive prompt",
        };
        var restoreCommand = new Command("restore", "Restore one verified retained snapshot");
        restoreCommand.Options.Add(jsonOption);
        restoreCommand.Options.Add(aiOption);
        restoreCommand.Options.Add(restoreSnapshotOption);
        restoreCommand.Options.Add(restoreToOption);
        restoreCommand.Options.Add(restoreModeOption);
        restoreCommand.Options.Add(restoreDryRunOption);
        restoreCommand.Options.Add(restoreYesOption);
        restoreCommand.Arguments.Add(restoreTargetArgument);
        restoreCommand.SetAction((parse, ct) =>
        {
            using var cancel = new CliCancelSource(ct);
            return Task.FromResult(RestoreCommand.Run(
                host,
                parse.GetValue(restoreTargetArgument) ?? [],
                parse.GetValue(restoreSnapshotOption) ?? "latest",
                parse.GetValue(restoreToOption),
                parse.GetValue(restoreModeOption) ?? "replace",
                parse.GetValue(restoreDryRunOption),
                parse.GetValue(restoreYesOption),
                CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption)),
                cancel.Token));
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

        var getGuiInstallerUrlOption = new Option<string?>("--installer-url")
        {
            Description = "Override the GUI installer download URL",
        };
        var getGuiForceOption = new Option<bool>("--force")
        {
            Description = "Reinstall even when the installed GUI is current",
        };
        var getGuiCustomHostOption = new Option<bool>("--allow-custom-host")
        {
            Description = "Allow an explicit HTTPS installer URL outside GitHub",
        };
        var getGuiCommand = new Command("gui", "Download and install the WinUI desktop app");
        getGuiCommand.Options.Add(aiOption);
        getGuiCommand.Options.Add(getGuiInstallerUrlOption);
        getGuiCommand.Options.Add(getGuiForceOption);
        getGuiCommand.Options.Add(getGuiCustomHostOption);
        getGuiCommand.SetAction(async (parse, ct) =>
        {
            var mode = CliOutputMode.From(json: false, ai: parse.GetValue(aiOption));
            return await GetGuiCommand.RunAsync(
                mode,
                parse.GetValue(getGuiInstallerUrlOption),
                parse.GetValue(getGuiForceOption),
                parse.GetValue(getGuiCustomHostOption),
                ct).ConfigureAwait(false);
        });

        var getCommand = new Command("get", "Download optional GSBT components");
        getCommand.Subcommands.Add(getGuiCommand);

        var diagnosticsOutputOption = new Option<string?>("--output")
        {
            Description = "Destination JSON file (defaults to Documents)",
        };
        var diagnosticsCommand = new Command("diagnostics", "Export redacted local operation diagnostics");
        diagnosticsCommand.Options.Add(jsonOption);
        diagnosticsCommand.Options.Add(aiOption);
        diagnosticsCommand.Options.Add(diagnosticsOutputOption);
        diagnosticsCommand.SetAction((parse, ct) => Task.FromResult(DiagnosticsCommand.Run(
            host,
            parse.GetValue(diagnosticsOutputOption),
            CliOutputMode.From(parse.GetValue(jsonOption), parse.GetValue(aiOption)))));

        var root = new RootCommand("Game Save Backup Tool — backup PC game saves from the terminal");
        root.Subcommands.Add(scanCommand);
        root.Subcommands.Add(listCommand);
        root.Subcommands.Add(backupCommand);
        root.Subcommands.Add(compressCommand);
        root.Subcommands.Add(verifyCommand);
        root.Subcommands.Add(restoreCommand);
        root.Subcommands.Add(settingsCommand);
        root.Subcommands.Add(addCommand);
        root.Subcommands.Add(statusCommand);
        if (guiInstalled)
        {
            root.Subcommands.Add(guiCommand);
        }
        else
        {
            root.Subcommands.Add(getCommand);
        }
        root.Subcommands.Add(diagnosticsCommand);
        root.Subcommands.Add(helpCommand);
        root.SetAction((_, ct) =>
        {
            CliHelpContent.WriteMainMenu();
            return Task.FromResult(0);
        });

        try
        {
            if (CliCommandSuggester.TryWriteRootSuggestion(
                    args,
                    args.Any(a => string.Equals(a, "--ai", StringComparison.OrdinalIgnoreCase))))
            {
                return 1;
            }

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
        var gui = CliInstallationState.GuiExecutablePath;
        if (!File.Exists(gui))
        {
            CliConsoleFormatter.WriteError(
                "GUI is not installed beside gsbt.exe. Install the full GSBT package to add gsbt-main.exe.");
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
