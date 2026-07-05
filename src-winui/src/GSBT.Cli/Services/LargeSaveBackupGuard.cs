using GSBT.Cli.Settings;
using GSBT.Core.Models;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Services;

/// <summary>Large/suspicious save-folder prompts before backup (WinUI parity).</summary>
public static class LargeSaveBackupGuard
{
    public sealed record FilterResult(
        IReadOnlyList<CatalogGameEntry> Approved,
        IReadOnlyList<LargeSaveSkip> Skipped);

    public sealed record LargeSaveSkip(string GameName, long Bytes, BackupSizeSeverity Severity, string Reason);

    public static FilterResult FilterForBackup(
        IReadOnlyList<CatalogGameEntry> entries,
        WinUiSettingsStore settings,
        bool nonInteractive)
    {
        var trusted = new HashSet<string>(
            settings.Get(LargeSavePathTrust.SettingsKey, new List<string>()),
            StringComparer.OrdinalIgnoreCase);
        var approved = new List<CatalogGameEntry>();
        var skipped = new List<LargeSaveSkip>();

        foreach (var entry in entries)
        {
            if (entry.SaveInRegistryOnly
                || string.IsNullOrWhiteSpace(entry.SavePathResolved)
                || !Directory.Exists(entry.SavePathResolved))
            {
                approved.Add(entry);
                continue;
            }

            var (bytes, _) = BackupFolderSizeEstimator.ComputeDirectoryMetrics(entry.SavePathResolved);
            var severity = LargeSavePathTrust.EffectiveSeverity(entry.GameName, bytes, trusted);
            if (severity is BackupSizeSeverity.Normal)
            {
                approved.Add(entry);
                continue;
            }

            if (nonInteractive)
            {
                skipped.Add(new LargeSaveSkip(
                    entry.GameName,
                    bytes,
                    severity,
                    FormatAiSkipReason(entry.GameName, bytes, severity)));
                continue;
            }

            if (!ConfirmInteractive(entry, bytes, severity))
            {
                skipped.Add(new LargeSaveSkip(
                    entry.GameName,
                    bytes,
                    severity,
                    "Skipped — large save folder not confirmed."));
                continue;
            }

            TrustGame(settings, trusted, entry.GameName);
            approved.Add(entry);
        }

        return new FilterResult(approved, skipped);
    }

    private static bool ConfirmInteractive(CatalogGameEntry entry, long bytes, BackupSizeSeverity severity)
    {
        var sizeText = BackupFolderSizeEstimator.FormatApproximateSizeIec(bytes);
        var tier = severity == BackupSizeSeverity.Suspicious ? "very large" : "unusually large";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]Large save folder[/] — [bold]{Markup.Escape(entry.GameName)}[/]");
        AnsiConsole.WriteLine($"  Size: {sizeText} ({tier} for typical game saves)");
        AnsiConsole.WriteLine($"  Path: {entry.SavePathResolved}");
        AnsiConsole.MarkupLine(
            "  [dim]This may be a wrong-era manifest match (install folder vs saves).[/]");
        return AnsiConsole.Confirm("  Back up this folder anyway?", defaultValue: false);
    }

    private static void TrustGame(WinUiSettingsStore settings, HashSet<string> trusted, string gameName)
    {
        if (!trusted.Add(gameName))
        {
            return;
        }

        settings.Set(LargeSavePathTrust.SettingsKey, trusted.ToList());
    }

    private static string FormatAiSkipReason(string gameName, long bytes, BackupSizeSeverity severity)
    {
        var size = BackupFolderSizeEstimator.FormatApproximateSizeIec(bytes);
        var tier = severity == BackupSizeSeverity.Suspicious ? "suspicious" : "large";
        return $"Skipped {tier} save folder ({size}) — use interactive backup to confirm, or add to trusted_large_save_paths.";
    }
}
