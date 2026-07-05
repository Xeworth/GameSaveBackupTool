using Spectre.Console;

namespace GSBT.Cli.Output;

public static class CliHelpContent
{
    public static void WriteMainMenu()
    {
        AnsiConsole.MarkupLine("[bold]Game Save Backup Tool (CLI)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  gsbt scan              Find games and save paths");
        AnsiConsole.WriteLine("  gsbt list              Show numbered game catalog");
        AnsiConsole.WriteLine("  gsbt backup            Backup game saves");
        AnsiConsole.WriteLine("  gsbt compress          Compress backup folder to .7z");
        AnsiConsole.WriteLine("  gsbt settings          View or change settings");
        AnsiConsole.WriteLine("  gsbt add custom        Add a custom game");
        AnsiConsole.WriteLine("  gsbt status            Paths and settings summary");
        AnsiConsole.WriteLine("  gsbt gui               Open the WinUI desktop app");
        AnsiConsole.WriteLine("  gsbt help              Command reference and guides");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Typical flow: [bold]gsbt scan[/] → [bold]gsbt list[/] → [bold]gsbt backup[/] → [bold]gsbt compress[/]");
    }

    public static void WriteHub()
    {
        AnsiConsole.MarkupLine("[bold]GSBT help[/]");
        AnsiConsole.WriteLine();
        WriteCommandSummary("scan", "Detect installed games and resolve save paths");
        WriteCommandSummary("list", "Show numbered catalog (default filter: found)");
        WriteCommandSummary("backup", "Copy saves to your backup folder");
        WriteCommandSummary("compress", "Compress backup data into a .7z archive");
        WriteCommandSummary("settings", "Backup path, compression, retention");
        WriteCommandSummary("add", "Add a custom game with a save folder");
        WriteCommandSummary("status", "Settings file, backup path, catalog count");
        WriteCommandSummary("gui", "Launch the WinUI desktop app");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]More detail[/]");
        AnsiConsole.WriteLine("  gsbt help backup");
        AnsiConsole.WriteLine("  gsbt help compress");
        AnsiConsole.WriteLine("  gsbt backup --help");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Automation / AI tools[/]");
        AnsiConsole.WriteLine("  --ai   JSON output + no progress UI + no interactive prompts");
        AnsiConsole.WriteLine("         Use on status, scan, list, backup, compress (implies --json)");
        AnsiConsole.WriteLine("         Example: gsbt backup --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Cancel[/]");
        AnsiConsole.WriteLine("  Press Ctrl+C during backup or compress to cancel gracefully.");
        AnsiConsole.WriteLine("  Partial .7z archives are removed on compress cancel.");
    }

    public static bool TryWriteCommandHelp(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        switch (command.Trim().ToLowerInvariant())
        {
            case "scan":
                WriteScanHelp();
                return true;
            case "list":
                WriteListHelp();
                return true;
            case "backup":
                WriteBackupHelp();
                return true;
            case "compress":
                WriteCompressHelp();
                return true;
            case "add":
                WriteAddHelp();
                return true;
            case "settings":
                WriteSettingsHelp();
                return true;
            case "status":
                WriteStatusHelp();
                return true;
            case "gui":
                WriteGuiHelp();
                return true;
            default:
                return false;
        }
    }

    private static void WriteCommandSummary(string name, string description) =>
        AnsiConsole.WriteLine($"  {name,-10} {description}");

    private static void WriteScanHelp()
    {
        WriteCommandHeader("scan", "Detect installed games and resolve save paths.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt scan");
        AnsiConsole.WriteLine("  gsbt scan --refresh-manifest");
        AnsiConsole.WriteLine("  gsbt scan --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Options:");
        AnsiConsole.WriteLine("  --refresh-manifest   Download latest Ludusavi manifest before scanning");
        AnsiConsole.WriteLine("  --ai                 Quiet run (minimal output, for scripts/agents)");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Run gsbt list after scan to see numbered games.");
    }

    private static void WriteListHelp()
    {
        WriteCommandHeader("list", "Show the numbered game catalog.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt list [found|not-found|all]");
        AnsiConsole.WriteLine("  gsbt list --json");
        AnsiConsole.WriteLine("  gsbt list --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Filters (default: found):");
        AnsiConsole.WriteLine("  found       Games with a located save path");
        AnsiConsole.WriteLine("  not-found   Games without a save path");
        AnsiConsole.WriteLine("  all         Entire catalog");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Columns include save folder size (may take a moment for large catalogs).");
        AnsiConsole.WriteLine("  --json / --ai skip size calculation for faster machine output.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Options:");
        AnsiConsole.WriteLine("  --json   Machine-readable JSON on stdout");
        AnsiConsole.WriteLine("  --ai     Same as --json (agent/script mode)");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Row numbers match the current filter. Run gsbt list before backup/compress");
        AnsiConsole.WriteLine("so indices stay in sync.");
    }

    private static void WriteBackupHelp()
    {
        WriteCommandHeader("backup", "Copy game saves to your backup folder.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt backup [targets...]");
        AnsiConsole.WriteLine("  gsbt backup 2");
        AnsiConsole.WriteLine("  gsbt backup 1,3,5");
        AnsiConsole.WriteLine("  gsbt backup 2-5");
        AnsiConsole.WriteLine("  gsbt backup \"elden ring\"");
        AnsiConsole.WriteLine("  gsbt backup --path \"D:\\Backups\" 3");
        AnsiConsole.WriteLine("  gsbt backup --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Targets (optional — omit to back up all eligible games):");
        AnsiConsole.WriteLine("  6              Row number from gsbt list");
        AnsiConsole.WriteLine("  1,3,5          Comma-separated row numbers");
        AnsiConsole.WriteLine("  2-5            Inclusive range");
        AnsiConsole.WriteLine("  elden ring     Fuzzy game name match");
        AnsiConsole.WriteLine("  mafia class, mafia def, lego star wars");
        AnsiConsole.WriteLine("                 Multiple names (comma-separated, quotes optional)");
        AnsiConsole.WriteLine("  trep, ho, sons Short fuzzy names can select multiple games");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Options:");
        AnsiConsole.WriteLine("  --path <dir>     Backup destination (overrides default)");
        AnsiConsole.WriteLine("  --set-default    Save --path as default_backup_path");
        AnsiConsole.WriteLine("  --yes            Accept suggested path without prompting");
        AnsiConsole.WriteLine("  --json           JSON result on stdout");
        AnsiConsole.WriteLine("  --ai             JSON + no progress UI + no prompts (implies --json)");
        AnsiConsole.WriteLine("                   Large save folders (>4–8 GiB) are auto-skipped;");
        AnsiConsole.WriteLine("                   tell the user or run interactively to confirm.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Large save folders:");
        AnsiConsole.WriteLine("  Folders over 4 GiB (8 GiB = suspicious) prompt y/n before backup.");
        AnsiConsole.WriteLine("  Confirming trusts the game in settings (same as WinUI).");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("First-time backup folder:");
        AnsiConsole.WriteLine("  Interactive TTY: prompted to choose a folder");
        AnsiConsole.WriteLine("  Scripts: use --path, --yes, gsbt settings backup-path, or --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Cancel: Ctrl+C stops between games; completed backups are kept.");
    }

    private static void WriteCompressHelp()
    {
        WriteCommandHeader("compress", "Compress backup folder contents into a single .7z archive.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt compress [targets...]");
        AnsiConsole.WriteLine("  gsbt compress 1,3,5-6");
        AnsiConsole.WriteLine("  gsbt compress \"elden ring\"");
        AnsiConsole.WriteLine("  gsbt compress --ai");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Targets (optional — omit to compress all eligible backed-up games):");
        AnsiConsole.WriteLine("  Same syntax as gsbt backup (row numbers, ranges, fuzzy names).");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Options:");
        AnsiConsole.WriteLine("  --json   JSON result on stdout");
        AnsiConsole.WriteLine("  --ai     JSON + no progress UI (implies --json)");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Compression settings: gsbt settings compression explain");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Cancel: Ctrl+C aborts compression and removes incomplete .7z files.");
    }

    private static void WriteAddHelp()
    {
        WriteCommandHeader("add custom", "Register a game with a save folder in the catalog.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt add custom \"Game Name\" \"C:\\path\\to\\saves\"");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("The folder must exist. Run gsbt list found to see the new entry.");
    }

    private static void WriteSettingsHelp()
    {
        WriteCommandHeader("settings", "View or change GSBT settings (shared with WinUI).");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt settings");
        AnsiConsole.WriteLine("  gsbt settings backup-path");
        AnsiConsole.WriteLine("  gsbt settings backup-path \"D:\\Backups\"");
        AnsiConsole.WriteLine("  gsbt settings compression show");
        AnsiConsole.WriteLine("  gsbt settings compression explain");
        AnsiConsole.WriteLine("  gsbt settings compression set mode smooth");
    }

    private static void WriteStatusHelp()
    {
        WriteCommandHeader("status", "Show backup path, compression, and catalog summary.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt status");
    }

    private static void WriteGuiHelp()
    {
        WriteCommandHeader("gui", "Launch gsbt-main.exe from the install folder.");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  gsbt gui");
    }

    private static void WriteCommandHeader(string name, string summary)
    {
        AnsiConsole.MarkupLine($"[bold]gsbt {name}[/] — {summary}");
    }
}
