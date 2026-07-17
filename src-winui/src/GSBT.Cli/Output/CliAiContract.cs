using System.Text.Json;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Cli.Output;

public static class CliAiContract
{
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
    };

    public static void WriteError(string command, string message, int exitCode, string? code = null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            command,
            success = false,
            exitCode,
            error = new
            {
                code = string.IsNullOrWhiteSpace(code) ? "error" : code,
                message,
            },
        }, JsonOptions));
    }

    public static object BuildStatusPayload(CliHost host)
    {
        var backupRoot = host.Settings.ResolveBackupDestination();
        var retention = host.Settings.Get("backup_retention_count", 3);
        var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
        var catalogCount = GetVisibleCatalogCount(host, backupRoot, subfolder);
        var installDir = AppContext.BaseDirectory;
        var guiPath = CliInstallationState.GuiExecutablePath;
        var sandboxPath = Path.Combine(installDir, "gsbt-sandbox.exe");
        var opts = CompressionOptionsResolver.FromSettings(
            host.Settings.Get,
            host.Settings.Get,
            host.Settings.Get);
        var manifest = host.ScanService.GetManifestProvenance();

        return new
        {
            schemaVersion = SchemaVersion,
            command = "status",
            success = true,
            version = AppVersionInfo.DisplayVersion,
            install = new
            {
                directory = installDir,
                cliExecutable = Path.Combine(installDir, "gsbt.exe"),
                guiExecutable = guiPath,
                guiInstalled = File.Exists(guiPath),
                sandboxExecutable = sandboxPath,
                sandboxInstalled = File.Exists(sandboxPath),
            },
            settingsFile = host.Settings.SettingsFilePath,
            catalogCount,
            dateFormat = host.Settings.Get("date_format", GSBT.Core.Common.BackupDateFormatter.DefaultFormatKey),
            backup = new
            {
                path = backupRoot,
                suggestedPath = host.Settings.GetBackupPathSuggestion(),
                configured = !string.IsNullOrWhiteSpace(backupRoot),
                exists = !string.IsNullOrWhiteSpace(backupRoot) && Directory.Exists(backupRoot),
                retention,
                subfolderPerGame = subfolder,
            },
            compression = new
            {
                engine = opts.Engine,
                level = opts.SevenMx,
                threads = opts.SevenMmt <= 0 ? "auto" : opts.SevenMmt.ToString(),
                mode = opts.SolidArchive ? "chunky" : "smooth",
                solidArchive = opts.SolidArchive,
                summary = opts.SummaryLabel,
                sevenZipReady = SevenZipNativeLibrary.IsAvailable,
                sevenZipError = SevenZipNativeLibrary.IsAvailable ? null : SevenZipNativeLibrary.LastError,
            },
            manifest = new
            {
                source = manifest.Source,
                version = manifest.Version,
                generatedAtUtc = manifest.GeneratedAtUtc,
                fetchedAtUtc = manifest.FetchedAtUtc,
                valid = manifest.IsValid,
                validation = manifest.ValidationStatus,
                sourceUrl = manifest.SourceUrl,
                sanitizedPathsRemoved = manifest.SanitizedPathsRemoved,
            },
        };
    }

    public static int GetVisibleCatalogCount(CliHost host, string? backupRoot, bool subfolderPerGame)
    {
        var dedupe = !host.Settings.Get("show_duplicate_save_titles", false);
        return CatalogGameEntryFactory.BuildSortedList(
            host.CatalogManager,
            backupRoot,
            subfolderPerGame,
            deduplicateSharedSaveFolders: dedupe).Count;
    }

    public static void WriteCapabilities()
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            command = "help",
            success = true,
            aiMode = new
            {
                flag = "--ai",
                behavior = "JSON result on stdout, newline-delimited progress events on stderr, no interactive prompts where supported.",
                stableExitCodes = new[]
                {
                    new { code = 0, meaning = "success" },
                    new { code = 1, meaning = "user-fixable input/configuration problem or partial failure" },
                    new { code = 2, meaning = "runtime/internal failure" },
                    new { code = 3, meaning = "unsafe restore target" },
                    new { code = 4, meaning = "insufficient restore space" },
                    new { code = 5, meaning = "snapshot verification failure" },
                    new { code = 6, meaning = "restore failed and original data was rolled back" },
                    new { code = 7, meaning = "restore or rollback requires manual recovery" },
                    new { code = 8, meaning = "partial restore" },
                    new { code = 130, meaning = "canceled" },
                },
            },
            agentNotebook = CliAgentNotebook.Content,
            commands = new object?[]
            {
                new
                {
                    name = "status",
                    forms = new[] { "gsbt status", "gsbt status --ai" },
                    aiSafe = true,
                    purpose = "Inspect settings, catalog count, backup path, and compression readiness.",
                },
                new
                {
                    name = "scan",
                    forms = new[] { "gsbt scan --ai", "gsbt scan --full --ai", "gsbt scan --refresh-manifest --full --ai" },
                    aiSafe = true,
                    purpose = "Detect installed games and update the local catalog.",
                },
                new
                {
                    name = "list",
                    forms = new[] { "gsbt list --ai", "gsbt list found --ai", "gsbt list all --ai", "gsbt list not-found --ai" },
                    aiSafe = true,
                    purpose = "Return indexed catalog rows. Use indexes or names as backup/compress targets.",
                },
                new
                {
                    name = "backup",
                    forms = new[] { "gsbt backup --ai", "gsbt backup 2 --ai", "gsbt backup 1,3,5 --ai", "gsbt backup trep, ho, sons --ai", "gsbt backup \"Game Name\" --ai" },
                    aiSafe = true,
                    purpose = "Copy selected save data into the configured backup folder.",
                    requires = new[] { "backup path configured or --path provided", "catalog contains backupable rows" },
                },
                new
                {
                    name = "compress",
                    forms = new[] { "gsbt compress --ai", "gsbt compress 1,3,5 --ai", "gsbt compress trep, ho, sons --ai", "gsbt compress \"Game Name\" --ai" },
                    aiSafe = true,
                    purpose = "Create a .7z archive from backed-up data.",
                    requires = new[] { "valid backup folder", "7z.dll ready", "compressible backed-up rows" },
                },
                new
                {
                    name = "verify",
                    forms = new[] { "gsbt verify --ai", "gsbt verify 2 --full --ai" },
                    aiSafe = true,
                    purpose = "Verify retained backup inventory, sizes, and optional SHA-256 content hashes.",
                },
                new
                {
                    name = "restore",
                    forms = new[] { "gsbt restore 2 --dry-run --ai", "gsbt restore 2 --snapshot latest --yes --ai" },
                    aiSafe = true,
                    purpose = "Preview or explicitly restore one verified snapshot with rollback protection.",
                    requires = new[] { "one backed-up target", "--yes for non-dry-run AI restores" },
                },
                new
                {
                    name = "add custom",
                    forms = new[]
                    {
                        "gsbt add custom \"Descriptive Name\" \"C:\\verified\\folder\" --ai",
                    },
                    aiSafe = true,
                    purpose = "Register any verified existing folder as a custom backup entry, including maps, mods, profiles, projects, or other user-selected data.",
                    requires = new[] { "folder exists and is reachable", "user approved the folder and descriptive name" },
                },
                new
                {
                    name = "settings",
                    forms = new[]
                    {
                        "gsbt settings",
                        "gsbt settings backup-path",
                        "gsbt settings backup-path \"D:\\Backups\"",
                        "gsbt settings compression show",
                        "gsbt settings --ai",
                        "gsbt settings compression set mode chunky",
                        "gsbt settings compression set mode smooth",
                    },
                    aiSafe = true,
                    purpose = "Inspect or mutate supported settings shared with the GUI.",
                },
                CliInstallationState.IsGuiInstalled ? new
                {
                    name = "gui",
                    forms = new[] { "gsbt gui" },
                    aiSafe = false,
                    purpose = "Launch the desktop GUI.",
                } : null,
                !CliInstallationState.IsGuiInstalled ? new
                {
                    name = "get gui",
                    forms = new[] { "gsbt get gui", "gsbt get gui --ai" },
                    aiSafe = true,
                    purpose = "Download and install the full WinUI GUI beside the CLI.",
                } : null,
                new
                {
                    name = "diagnostics",
                    forms = new[] { "gsbt diagnostics --ai", "gsbt diagnostics --output <file> --ai" },
                    aiSafe = true,
                    purpose = "Export path-redacted local operation history, environment, version, and manifest provenance.",
                },
            }.Where(command => command is not null).ToArray(),
            recommendedFlow = new[]
            {
                "gsbt status --ai",
                "gsbt scan --ai",
                "gsbt list --ai",
                "gsbt backup --ai",
                "gsbt compress --ai",
            },
        }, JsonOptions));
    }
}
