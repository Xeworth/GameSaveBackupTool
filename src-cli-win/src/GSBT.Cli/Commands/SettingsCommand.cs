using GSBT.Cli.Output;
using GSBT.Cli.Settings;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Commands;

public static class SettingsCommand
{
    public static int Run(CliHost host, IReadOnlyList<string> args)
    {
        CliConsoleFormatter.WriteCommandStart("gsbt settings");
        try
        {
            if (args.Count == 0 || string.Equals(args[0], "show", StringComparison.OrdinalIgnoreCase))
            {
                WriteShow(host.Settings);
                return 0;
            }

            if (string.Equals(args[0], "backup-path", StringComparison.OrdinalIgnoreCase))
            {
                return RunBackupPath(host.Settings, args.Skip(1).ToList());
            }

            if (string.Equals(args[0], "compression", StringComparison.OrdinalIgnoreCase))
            {
                return RunCompression(host.Settings, args.Skip(1).ToList());
            }

            CliConsoleFormatter.WriteError($"Unknown settings command: {args[0]}. Try gsbt settings show.");
            return 1;
        }
        finally
        {
            CliConsoleFormatter.WriteCommandEnd();
        }
    }

    private static void WriteShow(WinUiSettingsStore store)
    {
        var opts = CompressionOptionsResolver.FromSettings(store.Get, store.Get, store.Get);
        var mode = store.Get(CompressionOptionsResolver.SolidArchiveSettingsKey, true) ? "chunky (solid)" : "smooth (per-file)";

        AnsiConsole.MarkupLine("[bold]GSBT settings[/]");
        AnsiConsole.WriteLine($"  File              : {store.SettingsFilePath}");
        AnsiConsole.WriteLine($"  default_backup_path : {store.Get("default_backup_path", "")}");
        AnsiConsole.WriteLine($"  last_backup_path    : {store.Get("last_backup_path", "")}");
        AnsiConsole.WriteLine($"  backup_retention    : {store.Get("backup_retention_count", 3)}");
        AnsiConsole.WriteLine($"  subfolder_per_game  : {store.Get("backup_subfolder_per_game", true)}");
        AnsiConsole.WriteLine($"  compression level   : -mx={opts.SevenMx}");
        AnsiConsole.WriteLine($"  compression threads : {(opts.SevenMmt <= 0 ? "auto" : opts.SevenMmt.ToString())}");
        AnsiConsole.WriteLine($"  compression mode    : {mode}");
        AnsiConsole.WriteLine($"  compression summary : {opts.SummaryLabel}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]gsbt settings backup-path <path>  ·  gsbt settings compression explain[/]");
    }

    private static int RunBackupPath(WinUiSettingsStore store, IReadOnlyList<string> rest)
    {
        if (rest.Count == 0)
        {
            var current = store.Get("default_backup_path", string.Empty);
            if (string.IsNullOrWhiteSpace(current))
            {
                AnsiConsole.MarkupLine("[yellow]No default backup path set.[/]");
            }
            else
            {
                AnsiConsole.WriteLine(current);
            }

            return 0;
        }

        if (!BackupDestinationPolicy.TryNormalizePath(rest[0], out var normalized, out var err))
        {
            CliConsoleFormatter.WriteError(err ?? "Invalid path.");
            return 1;
        }

        store.Set("default_backup_path", normalized);
        AnsiConsole.MarkupLine($"[green]Default backup path set to[/] {Markup.Escape(normalized)}");
        return 0;
    }

    private static int RunCompression(WinUiSettingsStore store, IReadOnlyList<string> rest)
    {
        if (rest.Count == 0 || string.Equals(rest[0], "show", StringComparison.OrdinalIgnoreCase))
        {
            var opts = CompressionOptionsResolver.FromSettings(store.Get, store.Get, store.Get);
            var solid = store.Get(CompressionOptionsResolver.SolidArchiveSettingsKey, true);
            AnsiConsole.MarkupLine("[bold]Compression settings[/]");
            AnsiConsole.WriteLine($"  Level (-mx)   : {opts.SevenMx}");
            AnsiConsole.WriteLine($"  Threads (-mmt): {(opts.SevenMmt <= 0 ? "auto" : opts.SevenMmt.ToString())}");
            AnsiConsole.WriteLine($"  Mode (-ms)    : {(solid ? "chunky / solid block (on)" : "smooth / per-file (off)")}");
            AnsiConsole.WriteLine($"  Summary       : {opts.SummaryLabel}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]gsbt settings compression explain[/] for full reference");
            return 0;
        }

        if (string.Equals(rest[0], "explain", StringComparison.OrdinalIgnoreCase))
        {
            WriteCompressionExplain();
            return 0;
        }

        if (string.Equals(rest[0], "set", StringComparison.OrdinalIgnoreCase))
        {
            return RunCompressionSet(store, rest.Skip(1).ToList());
        }

        CliConsoleFormatter.WriteError("Usage: gsbt settings compression [show|explain|set ...]");
        return 1;
    }

    private static void WriteCompressionExplain()
    {
        AnsiConsole.MarkupLine("[bold]7-Zip compression reference[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  -mx (compression level):");
        AnsiConsole.WriteLine("    0 = store (no compression)");
        AnsiConsole.WriteLine("    1 = fast");
        AnsiConsole.WriteLine("    3 = low");
        AnsiConsole.WriteLine("    5 = normal (default)");
        AnsiConsole.WriteLine("    7 = high");
        AnsiConsole.WriteLine("    9 = maximum");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  -mmt (threads): 0 or omitted = auto (all logical cores)");
        AnsiConsole.WriteLine("  -ms (archive mode): on = solid block, off = per-file");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Dev notes (recommended):[/]");
        AnsiConsole.WriteLine("  chunky → solid block (-ms=on): smaller archives, jumpy % during compress;");
        AnsiConsole.WriteLine("           higher -mx levels matter more here.");
        AnsiConsole.WriteLine("  smooth → per-file (-ms=off): steady progress, per-game branch tracking;");
        AnsiConsole.WriteLine("           -mx1 vs -mx9 often yields similar size; mx1 is a bit faster.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]gsbt settings compression set level <0|1|3|5|7|9>[/]");
        AnsiConsole.MarkupLine("  [dim]gsbt settings compression set threads <N>[/]  (0 = auto)");
        AnsiConsole.MarkupLine("  [dim]gsbt settings compression set mode chunky|smooth[/]");
    }

    private static int RunCompressionSet(WinUiSettingsStore store, IReadOnlyList<string> rest)
    {
        if (rest.Count < 2)
        {
            CliConsoleFormatter.WriteError("Usage: gsbt settings compression set <level|threads|mode> <value>");
            return 1;
        }

        var key = rest[0].ToLowerInvariant();
        var value = rest[1];

        switch (key)
        {
            case "level":
            {
                if (!int.TryParse(value, out var mx) || !SevenZipCompressionLevelMapper.SupportedMxLevels.Contains(mx))
                {
                    CliConsoleFormatter.WriteError($"Level must be one of: {string.Join(", ", SevenZipCompressionLevelMapper.SupportedMxLevels)}");
                    return 1;
                }

                store.Set("compression_7z_level", mx);
                AnsiConsole.MarkupLine($"[green]Compression level set to[/] -mx={mx}");
                return 0;
            }
            case "threads":
            {
                if (!int.TryParse(value, out var threads) || threads < 0)
                {
                    CliConsoleFormatter.WriteError("Threads must be 0 (auto) or a positive integer.");
                    return 1;
                }

                var max = CompressionOptionsResolver.LogicalProcessorCount;
                if (threads > max)
                {
                    CliConsoleFormatter.WriteWarning($"Capping threads to {max} (logical processors).");
                    threads = max;
                }

                store.Set("compression_7z_threads", threads);
                AnsiConsole.MarkupLine($"[green]Compression threads set to[/] {(threads <= 0 ? "auto" : threads.ToString())}");
                return 0;
            }
            case "mode":
            {
                var solid = value.ToLowerInvariant() switch
                {
                    "chunky" or "solid" or "on" => true,
                    "smooth" or "per-file" or "perfile" or "off" => false,
                    _ => (bool?)null,
                };
                if (solid is null)
                {
                    CliConsoleFormatter.WriteError("Mode must be chunky or smooth.");
                    return 1;
                }

                store.Set(CompressionOptionsResolver.SolidArchiveSettingsKey, solid.Value);
                AnsiConsole.MarkupLine($"[green]Compression mode set to[/] {(solid.Value ? "chunky (solid)" : "smooth (per-file)")}");
                return 0;
            }
            default:
                CliConsoleFormatter.WriteError("Unknown setting. Use level, threads, or mode.");
                return 1;
        }
    }
}
