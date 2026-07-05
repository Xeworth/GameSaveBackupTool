using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>Persisted trust for unusually large save folders (WinUI parity).</summary>
public static class LargeSavePathTrust
{
    public const string SettingsKey = "trusted_large_save_paths";

    public static BackupSizeSeverity EffectiveSeverity(string gameName, long bytes, IReadOnlySet<string> trusted)
    {
        if (trusted.Contains(gameName))
        {
            return BackupSizeSeverity.Normal;
        }

        return BackupFolderSizeEstimator.Classify(bytes);
    }
}

/// <summary>Fills save-folder size on catalog rows for list display.</summary>
public static class CatalogSaveSizeEnricher
{
    public static IReadOnlyList<CatalogGameEntry> WithSaveSizes(
        IReadOnlyList<CatalogGameEntry> entries,
        Action<string>? onProgress = null)
    {
        if (entries.Count == 0)
        {
            return entries;
        }

        var diskCount = entries.Count(e =>
            !e.SaveInRegistryOnly && !string.IsNullOrWhiteSpace(e.SavePathResolved));
        if (diskCount > 8)
        {
            onProgress?.Invoke($"Computing save folder sizes ({diskCount} games)…");
        }

        var result = new List<CatalogGameEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.SaveInRegistryOnly)
            {
                result.Add(Copy(entry, null, "registry"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.SavePathResolved) || !Directory.Exists(entry.SavePathResolved))
            {
                result.Add(Copy(entry, null, "—"));
                continue;
            }

            var (bytes, _) = BackupFolderSizeEstimator.ComputeDirectoryMetrics(entry.SavePathResolved);
            var display = bytes <= 0
                ? "—"
                : BackupFolderSizeEstimator.FormatApproximateSizeIec(bytes);
            result.Add(Copy(entry, bytes, display));
        }

        return result;
    }

    private static CatalogGameEntry Copy(CatalogGameEntry entry, long? bytes, string display) =>
        new()
        {
            ListIndex = entry.ListIndex,
            GameName = entry.GameName,
            Platform = entry.Platform,
            SavePathRaw = entry.SavePathRaw,
            SavePathResolved = entry.SavePathResolved,
            SaveInRegistryOnly = entry.SaveInRegistryOnly,
            SaveRegistryHive = entry.SaveRegistryHive,
            SaveRegistrySubkey = entry.SaveRegistrySubkey,
            HasSaveLocation = entry.HasSaveLocation,
            SaveStatusLabel = entry.SaveStatusLabel,
            IsBackupable = entry.IsBackupable,
            IsCompressible = entry.IsCompressible,
            LastBackupIso = entry.LastBackupIso,
            LastBackupDisplay = entry.LastBackupDisplay,
            BackupSkipReason = entry.BackupSkipReason,
            CompressSkipReason = entry.CompressSkipReason,
            SaveSizeBytes = bytes,
            SaveSizeDisplay = display,
        };
}
