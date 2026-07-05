namespace GSBT.Core.Services;

/// <summary>One line in the pre-backup size summary dialog.</summary>
public sealed record BackupSizeEstimateEntry(
    string GameName,
    long Bytes,
    int FileCount,
    bool IsRegistryOnly,
    BackupSizeSeverity Severity,
    /// <summary>Resolved disk save root for UI (Explorer); null for registry-only rows.</summary>
    string? SaveFolderPath = null);

/// <summary>Totals for selected/effective backup candidates.</summary>
public sealed record BackupSizeEstimateSummary(
    long TotalBytes,
    int TotalFiles,
    int GamesInBackup,
    int SaveFoldersOnDisk,
    int RegistryOnlyCount,
    string BackupDestinationDisplay,
    IReadOnlyList<BackupSizeEstimateEntry> Entries)
{
    public bool HasSeverityWarnings => Entries.Any(e =>
        !e.IsRegistryOnly && (e.Severity == BackupSizeSeverity.Large || e.Severity == BackupSizeSeverity.Suspicious));
}

/// <summary>
/// Rough logical size of a directory tree (sum of file lengths). Used before backup to surface manifest mismatches (huge folders).
/// </summary>
public static class BackupFolderSizeEstimator
{
    /// <summary>Yellow tier — unusually large for typical saves.</summary>
    public const long LargeSaveThresholdBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Red tier — very large; often wrong-era manifest or install folder.</summary>
    public const long SuspiciousSaveThresholdBytes = 8L * 1024 * 1024 * 1024;

    public static BackupSizeSeverity Classify(long bytes)
    {
        if (bytes >= SuspiciousSaveThresholdBytes)
        {
            return BackupSizeSeverity.Suspicious;
        }

        if (bytes >= LargeSaveThresholdBytes)
        {
            return BackupSizeSeverity.Large;
        }

        return BackupSizeSeverity.Normal;
    }

    /// <summary>Walks all files under <paramref name="root"/>; skips files that cannot be read.</summary>
    public static long ComputeDirectoryLogicalSize(string root, CancellationToken cancellationToken = default) =>
        ComputeDirectoryMetrics(root, cancellationToken).Bytes;

    public static (long Bytes, int FileCount) ComputeDirectoryMetrics(string root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return (0, 0);
        }

        long sum = 0;
        var n = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                sum += new FileInfo(path).Length;
                n++;
            }
            catch
            {
                // ignore inaccessible files
            }
        }

        return (sum, n);
    }

    /// <summary>Decimal SI-style (matches older UI); use <see cref="FormatApproximateSizeIec"/> for MiB/GiB.</summary>
    public static string FormatApproximateSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double v = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        var order = -1;
        do
        {
            v /= 1024;
            order++;
        } while (v >= 1024 && order < units.Length - 1);

        return $"{v:0.##} {units[order]}";
    }

    /// <summary>Binary IEC units (MiB, GiB) for estimate dialogs.</summary>
    public static string FormatApproximateSizeIec(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        const double kib = 1024;
        var v = (double)bytes;
        if (v < kib * kib)
        {
            return $"{v / kib:0.#} KiB";
        }

        if (v < kib * kib * kib)
        {
            return $"{v / (kib * kib):0.##} MiB";
        }

        if (v < kib * kib * kib * kib)
        {
            return $"{v / (kib * kib * kib):0.##} GiB";
        }

        return $"{v / (kib * kib * kib * kib):0.##} TiB";
    }
}

public enum BackupSizeSeverity
{
    Normal,
    Large,
    Suspicious
}
