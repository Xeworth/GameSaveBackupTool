using System.Text.Json;
using GSBT.Cli.Services;
using GSBT.Core.Services;

namespace GSBT.Cli.Output;

public static class CliAiContract
{
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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
        var guiPath = Path.Combine(installDir, "gsbt-main.exe");
        var sandboxPath = Path.Combine(installDir, "gsbt-sandbox.exe");
        var opts = CompressionOptionsResolver.FromSettings(
            host.Settings.Get,
            host.Settings.Get,
            host.Settings.Get);

        return new
        {
            schemaVersion = SchemaVersion,
            command = "status",
            success = true,
            install = new
            {
                directory = installDir,
                cliExecutable = Path.Combine(installDir, "gsbt.exe"),
                guiExecutable = guiPath,
                guiInstalled = File.Exists(guiPath),
                sandboxExecutable = sandboxPath,
                sandboxInstalled = File.Exists(sandboxPath),
                cliInstallScript = GitHubReleaseAssets.CliInstallScriptUrl,
                guiInstallScript = GitHubReleaseAssets.GuiInstallScriptUrl,
                guiUpgradeCommand = File.Exists(guiPath) ? null : "gsbt get gui",
            },
            settingsFile = host.Settings.SettingsFilePath,
            catalogCount,
            backup = new
            {
                path = backupRoot,
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
                behavior = "JSON output, no progress UI, no interactive prompts where supported.",
                stableExitCodes = new[]
                {
                    new { code = 0, meaning = "success" },
                    new { code = 1, meaning = "user-fixable input/configuration problem or partial failure" },
                    new { code = 2, meaning = "runtime/internal failure" },
                    new { code = 130, meaning = "canceled" },
                },
            },
            commands = new object[]
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
                    forms = new[] { "gsbt scan --ai", "gsbt scan --refresh-manifest --ai" },
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
                    name = "settings",
                    forms = new[]
                    {
                        "gsbt settings",
                        "gsbt settings backup-path",
                        "gsbt settings backup-path \"D:\\Backups\"",
                        "gsbt settings compression show",
                        "gsbt settings compression set mode chunky",
                        "gsbt settings compression set mode smooth",
                    },
                    aiSafe = false,
                    purpose = "Inspect or mutate settings shared with the GUI. Human-readable output for now.",
                },
                new
                {
                    name = "get",
                    forms = new[] { "gsbt get gui", "gsbt get gui --force", "gsbt get gui --ai" },
                    aiSafe = true,
                    purpose = "Download the latest WinUI GUI installer from GitHub and run it silently.",
                },
                new
                {
                    name = "gui",
                    forms = new[] { "gsbt gui" },
                    aiSafe = false,
                    purpose = "Launch the desktop GUI.",
                },
            },
            install = new
            {
                cliScript = GitHubReleaseAssets.CliInstallScriptUrl,
                guiScript = GitHubReleaseAssets.GuiInstallScriptUrl,
            },
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
