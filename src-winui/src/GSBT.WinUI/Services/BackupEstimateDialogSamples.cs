using System.IO.Pipes;
using System.Text;
using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

internal static class BackupEstimateDialogSamples
{
    public static BackupSizeEstimateSummary YellowTierPreview(string backupDestinationDisplay) =>
        Build(backupDestinationDisplay, "Game B (preview)", BackupFolderSizeEstimator.LargeSaveThresholdBytes, BackupSizeSeverity.Large);

    public static BackupSizeEstimateSummary RedTierPreview(string backupDestinationDisplay) =>
        Build(
            backupDestinationDisplay,
            "Game C (preview)",
            BackupFolderSizeEstimator.SuspiciousSaveThresholdBytes + 1024L * 1024,
            BackupSizeSeverity.Suspicious);

    private static BackupSizeEstimateSummary Build(
        string dest,
        string gameName,
        long bytes,
        BackupSizeSeverity severity)
    {
        var e = new BackupSizeEstimateEntry(gameName, bytes, 1, false, severity, @"C:\GSBT\SandboxSimulation\preview");
        return new BackupSizeEstimateSummary(bytes, 1, 1, 1, 0, dest, [e]);
    }
}
