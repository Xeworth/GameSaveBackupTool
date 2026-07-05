using GSBT.Core.Catalog;
using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>Builds <see cref="CatalogGameEntry"/> rows from the on-disk catalog (WinUI parity).</summary>
public static class CatalogGameEntryFactory
{
    public const string SaveStatusFound = "Found";
    public const string SaveStatusNotFound = "Not found";

    public static IReadOnlyList<CatalogGameEntry> BuildSortedList(
        SaveCatalogManager catalogManager,
        string? backupRoot,
        bool subfolderPerGame,
        bool deduplicateSharedSaveFolders = false)
    {
        var names = catalogManager.Catalog.Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new List<CatalogGameEntry>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (!catalogManager.Catalog.TryGetValue(name, out var row))
            {
                continue;
            }

            var entry = FromCatalogRow(catalogManager, name, row, backupRoot, subfolderPerGame);
            if (entry is null)
            {
                continue;
            }

            list.Add(new CatalogGameEntry
            {
                ListIndex = i + 1,
                GameName = entry.GameName,
                Platform = entry.Platform,
                IsUserAdded = entry.IsUserAdded,
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
            });
        }

        if (deduplicateSharedSaveFolders)
        {
            list = DeduplicateSharedSaveFolders(list).ToList();
        }

        return Reindex(list);
    }

    internal static CatalogGameEntry? FromCatalogRow(
        SaveCatalogManager catalogManager,
        string gameName,
        Dictionary<string, object?> row,
        string? backupRoot,
        bool subfolderPerGame)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var regOnly = CatalogUserAdded.CoerceBool(row.GetValueOrDefault("save_in_registry_only"));
        var hive = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_hive"));
        var sub = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_subkey"));
        var rawPath = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_path"));
        var isUser = CatalogUserAdded.IsUserAddedEntry(row);
        var steamAppId = CatalogUserAdded.CoerceString(row.GetValueOrDefault("steam_app_id"));
        var platform = ResolveCatalogPlatform(row, isUser, steamAppId);
        var installPath = CatalogUserAdded.CoerceString(row.GetValueOrDefault("install_path"));

        string? resolved = null;
        if (!regOnly && !string.IsNullOrWhiteSpace(rawPath))
        {
            resolved = catalogManager.ResolvePath(rawPath, installPath);
        }

        var hasLoc = GameCatalogFilter.HasSaveLocation(resolved, regOnly);
        var status = hasLoc ? SaveStatusFound : SaveStatusNotFound;
        var lastBkRaw = CatalogUserAdded.CoerceString(row.GetValueOrDefault("last_backup"));
        var lastBkDisplay = string.IsNullOrWhiteSpace(lastBkRaw) ? "Not yet" : FormatLastBackup(lastBkRaw);

        var isBackupable = ComputeIsBackupable(regOnly, resolved, hive, sub, out var backupSkip);
        var isCompressible = !string.IsNullOrWhiteSpace(backupRoot)
            && BackupRetentionVerifier.HasRetentionArtifact(backupRoot, gameName, subfolderPerGame);
        string? compressSkip = null;
        if (!isCompressible)
        {
            compressSkip = string.IsNullOrWhiteSpace(backupRoot)
                ? "Backup destination is not set."
                : "No backup folders found for this game. Run backup first.";
        }

        return new CatalogGameEntry
        {
            ListIndex = 0,
            GameName = gameName,
            Platform = platform,
            IsUserAdded = isUser,
            SavePathRaw = rawPath,
            SavePathResolved = resolved,
            SaveInRegistryOnly = regOnly,
            SaveRegistryHive = hive,
            SaveRegistrySubkey = sub,
            HasSaveLocation = hasLoc,
            SaveStatusLabel = status,
            IsBackupable = isBackupable,
            IsCompressible = isCompressible,
            LastBackupIso = lastBkRaw,
            LastBackupDisplay = lastBkDisplay,
            BackupSkipReason = backupSkip,
            CompressSkipReason = compressSkip,
        };
    }

    private static IReadOnlyList<CatalogGameEntry> DeduplicateSharedSaveFolders(IReadOnlyList<CatalogGameEntry> entries)
    {
        var scanRows = entries
            .Where(e => !e.IsUserAdded)
            .Select(e => new SaveScanResult
            {
                RowId = e.GameName,
                Name = e.GameName,
                Platform = e.Platform,
                SavePathRaw = e.SavePathRaw,
                SavePathResolved = e.SavePathResolved,
                SaveInRegistryOnly = e.SaveInRegistryOnly,
                SaveRegistryHive = e.SaveRegistryHive,
                SaveRegistrySubkey = e.SaveRegistrySubkey,
                Source = "Catalog",
                WallSec = 0,
                ScanOutcome = e.HasSaveLocation ? "SAVE_ON_DISK" : "NO_MANIFEST_PATHS",
            })
            .ToList();

        if (scanRows.Count < 2)
        {
            return entries;
        }

        var (_, droppedNames) = GameScanPostProcessor.DeduplicateBySharedSaveRoot(scanRows);
        if (droppedNames.Count == 0)
        {
            return entries;
        }

        var dropped = droppedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Where(e => e.IsUserAdded || !dropped.Contains(e.GameName)).ToList();
    }

    private static IReadOnlyList<CatalogGameEntry> Reindex(IReadOnlyList<CatalogGameEntry> entries)
    {
        var list = new List<CatalogGameEntry>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            list.Add(new CatalogGameEntry
            {
                ListIndex = i + 1,
                GameName = e.GameName,
                Platform = e.Platform,
                IsUserAdded = e.IsUserAdded,
                SavePathRaw = e.SavePathRaw,
                SavePathResolved = e.SavePathResolved,
                SaveInRegistryOnly = e.SaveInRegistryOnly,
                SaveRegistryHive = e.SaveRegistryHive,
                SaveRegistrySubkey = e.SaveRegistrySubkey,
                HasSaveLocation = e.HasSaveLocation,
                SaveStatusLabel = e.SaveStatusLabel,
                IsBackupable = e.IsBackupable,
                IsCompressible = e.IsCompressible,
                LastBackupIso = e.LastBackupIso,
                LastBackupDisplay = e.LastBackupDisplay,
                BackupSkipReason = e.BackupSkipReason,
                CompressSkipReason = e.CompressSkipReason,
                SaveSizeBytes = e.SaveSizeBytes,
                SaveSizeDisplay = e.SaveSizeDisplay,
            });
        }

        return list;
    }

    private static bool ComputeIsBackupable(
        bool regOnly,
        string? resolvedPath,
        string? hive,
        string? subkey,
        out string? skipReason)
    {
        skipReason = null;
        if (!regOnly)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                skipReason = "No save folder configured.";
                return false;
            }

            if (!Directory.Exists(resolvedPath))
            {
                skipReason = $"Save folder does not exist: {resolvedPath}";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(hive) || string.IsNullOrWhiteSpace(subkey))
        {
            skipReason = "Registry save is not fully configured.";
            return false;
        }

        if (!RegistrySaveBackupService.TryComputeSnapshotFingerprint(hive, subkey, out _))
        {
            skipReason = "Registry save key is not available.";
            return false;
        }

        return true;
    }

    private static string FormatLastBackup(string iso)
    {
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        return iso;
    }

    private static string ResolveCatalogPlatform(
        Dictionary<string, object?> row,
        bool isUser,
        string? steamAppId)
    {
        var platform = CatalogUserAdded.CoerceString(row.GetValueOrDefault("platform"));
        if (!GamePlatformHeuristics.IsUnknownOrEmpty(platform))
        {
            return platform!;
        }

        if (isUser)
        {
            return "Custom";
        }

        if (!string.IsNullOrWhiteSpace(steamAppId))
        {
            return "Steam";
        }

        var installPath = CatalogUserAdded.CoerceString(row.GetValueOrDefault("install_path"));
        var inferred = GamePlatformHeuristics.InferFromInstallPath(installPath);
        if (!string.IsNullOrWhiteSpace(inferred))
        {
            return inferred;
        }

        return GamePlatformHeuristics.OtherLabel;
    }
}
