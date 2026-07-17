using System.Text.Json;
using GSBT.Core.Common;
using GSBT.Core.Catalog;
using GSBT.Core.Models;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Output;

public static class CliConsoleFormatter
{
    public static void WriteCommandStart(string commandName)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(commandName)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void WriteCommandEnd() => AnsiConsole.WriteLine();

    public static void WriteListTable(IReadOnlyList<CatalogGameEntry> entries, GameCatalogFilterMode filterMode)
    {
        if (entries.Count == 0)
        {
            var hint = filterMode == GameCatalogFilterMode.FoundOnly
                ? "No games with saves found. Try [bold]gsbt list all[/] or run [bold]gsbt scan[/]."
                : "No games in catalog. Run [bold]gsbt scan[/] first.";
            AnsiConsole.MarkupLine($"[yellow]{hint}[/]");
            return;
        }

        var filterLabel = filterMode switch
        {
            GameCatalogFilterMode.FoundOnly => "found",
            GameCatalogFilterMode.NotFoundOnly => "not found",
            _ => "all",
        };
        AnsiConsole.MarkupLine($"[dim]Filter: {filterLabel} ({entries.Count} game(s))[/]");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#", c => c.RightAligned())
            .AddColumn("Game")
            .AddColumn("Save")
            .AddColumn("Size", c => c.RightAligned())
            .AddColumn("Backup")
            .AddColumn("Last backup");

        foreach (var e in entries)
        {
            var backup = e.IsBackupable ? "[green]yes[/]" : "[red]no[/]";
            table.AddRow(
                e.ListIndex.ToString(),
                Markup.Escape(e.GameName),
                Markup.Escape(e.SaveStatusLabel),
                Markup.Escape(e.SaveSizeDisplay ?? "—"),
                backup,
                Markup.Escape(e.LastBackupDisplay));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void WriteJsonList(IReadOnlyList<CatalogGameEntry> entries, string filterToken, bool ai = false)
    {
        var games = entries.Select(e => new
        {
            index = e.ListIndex,
            game = e.GameName,
            platform = e.Platform,
            saveStatus = e.SaveStatusLabel,
            saveSizeBytes = e.SaveSizeBytes,
            saveSizeDisplay = e.SaveSizeDisplay,
            backupable = e.IsBackupable,
            compressible = e.IsCompressible,
            lastBackup = e.LastBackupIso,
            backupSkipReason = e.BackupSkipReason,
            compressSkipReason = e.CompressSkipReason,
        });

        object payload = ai
            ? new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "list",
                success = true,
                filter = filterToken,
                count = entries.Count,
                games,
                nextActions = new[]
                {
                    "Use index values as targets for gsbt backup --ai or gsbt compress --ai.",
                    "Run gsbt list all --ai to include not-found rows.",
                },
            }
            : new
            {
                filter = filterToken,
                count = entries.Count,
                games,
            };
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WriteBackupResults(
        IReadOnlyList<BackupItemResult> results,
        bool asJson,
        bool branchRendered = false,
        bool canceled = false)
    {
        if (asJson)
        {
            var ok = results.Count(r => r.Success);
            var payload = new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "backup",
                success = !canceled && results.Count > 0 && results.All(r => r.Success),
                canceled,
                ok,
                failed = results.Count - ok,
                results = results.Select(r => new
                {
                    game = r.GameName,
                    success = r.Success,
                    skipped = r.Skipped,
                    backupPath = r.BackupPath,
                    runId = r.RunId,
                    filesCopied = r.FilesCopied,
                    bytesCopied = r.BytesCopied,
                    warnings = r.Warnings,
                    error = r.Error,
                }),
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        if (branchRendered)
        {
            return;
        }

        foreach (var r in results)
        {
            if (r.Success)
            {
                AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(r.GameName)} → {Markup.Escape(r.BackupPath ?? "")}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]FAIL[/] {Markup.Escape(r.GameName)}: {Markup.Escape(r.Error ?? "Unknown error")}");
            }
        }

        var succeeded = results.Count(r => r.Success);
        AnsiConsole.WriteLine();
        var suffix = canceled ? " (canceled)" : string.Empty;
        AnsiConsole.MarkupLine($"Backed up [bold]{succeeded}[/]/{results.Count} game(s){suffix}.");
    }

    public static void WriteCompressCanceled(IReadOnlyList<string> selectedGames)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = CliAiContract.SchemaVersion,
            command = "compress",
            success = false,
            canceled = true,
            message = "Compression canceled.",
            selectedGames,
        }, JsonOptions));
    }

    public static void WriteCompressResult(CompressRunResult result, bool asJson, bool branchRendered = false)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "compress",
                success = result.Success,
                message = result.Message,
                archivePath = result.ArchivePath,
                selectedGames = result.SelectedGames,
                compression = new
                {
                    mode = result.CompressionMode,
                    level = result.CompressionLevel,
                    threads = result.CompressionThreads,
                    solidArchive = result.SolidArchive,
                },
                metrics = new
                {
                    elapsedSeconds = result.ElapsedSeconds,
                    inputBytes = result.InputBytes,
                    archiveBytes = result.ArchiveBytes,
                    archivePercentOfInput = result.InputBytes > 0
                        ? Math.Round(result.ArchiveBytes * 100.0 / result.InputBytes, 2)
                        : (double?)null,
                },
            }, JsonOptions));
            return;
        }

        if (branchRendered)
        {
            return;
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Message)}[/]");
        }
    }

    public static void WriteStatus(CliHost host)
    {
        var backupRoot = host.Settings.ResolveBackupDestination();
        var retention = host.Settings.Get("backup_retention_count", 3);
        var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
        var opts = CompressionOptionsResolver.FromSettings(
            host.Settings.Get,
            host.Settings.Get,
            host.Settings.Get);
        var count = CliAiContract.GetVisibleCatalogCount(host, backupRoot, subfolder);
        var guiInstalled = CliInstallationState.IsGuiInstalled;
        var dateFormat = host.Settings.Get("date_format", GSBT.Core.Common.BackupDateFormatter.DefaultFormatKey);
        var manifest = host.ScanService.GetManifestProvenance();

        AnsiConsole.WriteLine($"  Version       : {AppVersionInfo.DisplayVersion}");
        AnsiConsole.WriteLine($"  Settings file : {host.Settings.SettingsFilePath}");
        AnsiConsole.WriteLine($"  Catalog games : {count}");
        AnsiConsole.WriteLine($"  GUI           : {(guiInstalled ? "installed" : "not installed - run gsbt get gui")}");
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            AnsiConsole.WriteLine($"  Suggested     : {host.Settings.GetBackupPathSuggestion()}");
        }
        AnsiConsole.WriteLine($"  Backup folder : {backupRoot ?? "(not set — run gsbt settings backup-path or gsbt backup)"}");
        AnsiConsole.WriteLine($"  Retention     : {retention}");
        AnsiConsole.WriteLine($"  Subfolder/game: {(subfolder ? "yes" : "no")}");
        AnsiConsole.WriteLine($"  Date format   : {dateFormat}");
        AnsiConsole.WriteLine($"  Compression   : {opts.SummaryLabel}");
        AnsiConsole.WriteLine($"  7z engine     : {(SevenZipNativeLibrary.IsAvailable ? "ready" : SevenZipNativeLibrary.LastError ?? "unavailable")}");
        var manifestSanitized = manifest.SanitizedPathsRemoved > 0
            ? $", {manifest.SanitizedPathsRemoved} unsafe path(s) removed"
            : string.Empty;
        AnsiConsole.WriteLine($"  Manifest      : {manifest.Source}, {manifest.ValidationStatus}{manifestSanitized}");
    }

    public static void WriteStatusJson(CliHost host, bool ai)
    {
        var payload = ai
            ? CliAiContract.BuildStatusPayload(host)
            : new
            {
                version = AppVersionInfo.DisplayVersion,
                settingsFile = host.Settings.SettingsFilePath,
                catalogCount = CliAiContract.GetVisibleCatalogCount(
                    host,
                    host.Settings.ResolveBackupDestination(),
                    host.Settings.Get("backup_subfolder_per_game", true)),
                backupFolder = host.Settings.ResolveBackupDestination(),
                guiInstalled = CliInstallationState.IsGuiInstalled,
                dateFormat = host.Settings.Get("date_format", GSBT.Core.Common.BackupDateFormatter.DefaultFormatKey),
                compression = CompressionOptionsResolver.FromSettings(
                    host.Settings.Get,
                    host.Settings.Get,
                    host.Settings.Get).SummaryLabel,
                sevenZipReady = SevenZipNativeLibrary.IsAvailable,
                sevenZipError = SevenZipNativeLibrary.IsAvailable ? null : SevenZipNativeLibrary.LastError,
                manifest = host.ScanService.GetManifestProvenance(),
            };
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WriteError(string message) =>
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");

    public static void WriteWarning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(message)}");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed class BackupItemResult
{
    public required string GameName { get; init; }

    public bool Success { get; init; }

    public bool Skipped { get; init; }

    public string? BackupPath { get; init; }

    public string? Error { get; init; }

    public string? RunId { get; init; }

    public int FilesCopied { get; init; }

    public long BytesCopied { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class CompressRunResult
{
    public bool Success { get; init; }

    public required string Message { get; init; }

    public string? ArchivePath { get; init; }

    public IReadOnlyList<string> SelectedGames { get; init; } = [];

    public string CompressionMode { get; init; } = string.Empty;

    public int CompressionLevel { get; init; }

    public string CompressionThreads { get; init; } = string.Empty;

    public bool SolidArchive { get; init; }

    public double ElapsedSeconds { get; init; }

    public long InputBytes { get; init; }

    public long ArchiveBytes { get; init; }
}
