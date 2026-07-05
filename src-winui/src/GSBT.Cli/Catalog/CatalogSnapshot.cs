using System.Text.Json;
using GSBT.Core.Catalog;
using GSBT.Core.Common;
using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.Cli.Catalog;

public sealed class CatalogSnapshot
{
    private const string SnapshotFileName = "cli_list_snapshot.json";

    public IReadOnlyList<CatalogGameEntry> Entries { get; init; } = [];

    public DateTimeOffset? SnapshotUtc { get; init; }

    public GameCatalogFilterMode FilterMode { get; init; } = CatalogListFilter.DefaultMode;

    public static string SnapshotPath => Path.Combine(UserDataDir.GetWinUiUserDataDir(), SnapshotFileName);

    public static CatalogSnapshot Build(CliHost host, GameCatalogFilterMode filterMode) =>
        Create(host, filterMode, persist: true);

    public static CatalogSnapshot LoadCurrent(CliHost host)
    {
        var filter = ReadPersistedFilterMode();
        return Create(host, filter, persist: false);
    }

    private static CatalogSnapshot Create(CliHost host, GameCatalogFilterMode filterMode, bool persist)
    {
        var backupRoot = host.Settings.ResolveBackupDestination();
        var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
        var dedupe = !host.Settings.Get("show_duplicate_save_titles", false);
        var all = CatalogGameEntryFactory.BuildSortedList(
            host.CatalogManager,
            backupRoot,
            subfolder,
            deduplicateSharedSaveFolders: dedupe);
        var filtered = ApplyFilter(all, filterMode);
        var snap = new CatalogSnapshot
        {
            Entries = filtered,
            SnapshotUtc = DateTimeOffset.UtcNow,
            FilterMode = filterMode,
        };

        if (persist)
        {
            snap.Persist();
        }

        return snap;
    }

    public static IReadOnlyList<CatalogGameEntry> ApplyFilter(
        IReadOnlyList<CatalogGameEntry> all,
        GameCatalogFilterMode mode)
    {
        var filtered = new List<CatalogGameEntry>();
        foreach (var entry in all)
        {
            if (!GameCatalogFilter.IncludeRow(mode, entry.HasSaveLocation))
            {
                continue;
            }

            filtered.Add(new CatalogGameEntry
            {
                ListIndex = filtered.Count + 1,
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
                SaveSizeBytes = entry.SaveSizeBytes,
                SaveSizeDisplay = entry.SaveSizeDisplay,
            });
        }

        return filtered;
    }

    private static GameCatalogFilterMode ReadPersistedFilterMode()
    {
        try
        {
            if (!File.Exists(SnapshotPath))
            {
                return CatalogListFilter.DefaultMode;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(SnapshotPath));
            if (doc.RootElement.TryGetProperty("filter", out var filterEl)
                && filterEl.ValueKind == JsonValueKind.String
                && CatalogListFilter.TryParse(filterEl.GetString(), out var mode))
            {
                return mode;
            }
        }
        catch
        {
            // use default
        }

        return CatalogListFilter.DefaultMode;
    }

    private void Persist()
    {
        try
        {
            var payload = new
            {
                capturedAtUtc = SnapshotUtc?.ToString("O"),
                filter = CatalogListFilter.ToToken(FilterMode),
                gameNames = Entries.Select(e => e.GameName).ToList(),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best-effort
        }
    }
}
