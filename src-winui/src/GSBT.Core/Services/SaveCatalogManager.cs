using System.Text.Json;
using GSBT.Core.Catalog;
using GSBT.Core.Common;

namespace GSBT.Core.Services;

public sealed class SaveCatalogManager
{
    private readonly object _lock = new();
    private bool _dirty;
    private DateTime _loadedWriteUtc;
    private long _loadedLength;

    public string CatalogPath { get; }
    public string CatalogMetadataPath => CatalogPath + ".meta.json";
    public string? LegacyCatalogPath { get; }
    public Dictionary<string, Dictionary<string, object?>> Catalog { get; private set; }

    /// <param name="skipInitialDiskLoad">When true, start with an empty in-memory catalog (no JSON read). Disk is written on first <see cref="Flush"/>.</param>
    /// <param name="importLegacyCatalogIfMissing">When true and the primary catalog file is missing, import legacy Python-era / dev <c>config/game_save_data.json</c> once.</param>
    public SaveCatalogManager(
        string? catalogPath = null,
        string? legacyCatalogPath = null,
        bool skipInitialDiskLoad = false,
        bool importLegacyCatalogIfMissing = false)
    {
        SkipInitialDiskLoad = skipInitialDiskLoad;
        ImportLegacyCatalogIfMissing = importLegacyCatalogIfMissing;

        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            CatalogPath = catalogPath!;
            LegacyCatalogPath = null;
        }
        else
        {
            var appData = UserDataDir.GetWinUiUserDataDir();
            CatalogPath = Path.Combine(appData, "game_save_data.json");
            LegacyCatalogPath = legacyCatalogPath ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "game_save_data.json"));
        }

        Catalog = LoadCatalog();
        CaptureFileStampUnsafe();
    }

    /// <summary>When true, <see cref="LoadCatalog"/> did not read disk (fresh session until first persist).</summary>
    public bool SkipInitialDiskLoad { get; }

    public bool ImportLegacyCatalogIfMissing { get; }

    public void AddOrUpdate(string gameName, Dictionary<string, object?> payload)
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            Catalog[gameName] = payload;
            _dirty = true;
            PersistUnsafe();
        }
    }

    /// <summary>Normalize detected catalog keys to human-facing names and merge old raw trademark variants.</summary>
    public int NormalizeDetectedDisplayNames()
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            if (Catalog.Count == 0)
            {
                return 0;
            }

            var normalized = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            var changed = 0;
            foreach (var (key, row) in Catalog)
            {
                if (CatalogUserAdded.IsUserAddedEntry(row))
                {
                    AddPreservingUserRows(normalized, key, row);
                    continue;
                }

                var clean = GameDisplayName.CleanDisplayName(key);
                var targetKey = string.IsNullOrWhiteSpace(clean) ? key : clean;
                if (!string.Equals(key, targetKey, StringComparison.Ordinal))
                {
                    changed++;
                }

                if (normalized.TryGetValue(targetKey, out var existing)
                    && CatalogUserAdded.IsUserAddedEntry(existing))
                {
                    AddPreservingUserRows(normalized, key, row);
                    continue;
                }

                if (normalized.TryGetValue(targetKey, out existing))
                {
                    normalized[targetKey] = MergeCatalogRows(existing, row);
                    changed++;
                }
                else
                {
                    normalized[targetKey] = row;
                }
            }

            if (changed == 0)
            {
                return 0;
            }

            Catalog = normalized;
            _dirty = true;
            PersistUnsafe();
            return changed;
        }
    }

    private static void AddPreservingUserRows(
        Dictionary<string, Dictionary<string, object?>> normalized,
        string key,
        Dictionary<string, object?> row)
    {
        if (!normalized.ContainsKey(key))
        {
            normalized[key] = row;
            return;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{key} ({suffix++})";
        }
        while (normalized.ContainsKey(candidate));

        normalized[candidate] = row;
    }

    private static Dictionary<string, object?> MergeCatalogRows(
        Dictionary<string, object?> existing,
        Dictionary<string, object?> incoming)
    {
        var useIncoming = PreferRow(incoming, existing);
        var merged = useIncoming
            ? new Dictionary<string, object?>(incoming)
            : new Dictionary<string, object?>(existing);
        var fallback = useIncoming ? existing : incoming;

        foreach (var (key, value) in fallback)
        {
            if (!merged.TryGetValue(key, out var current) || IsEmptyValue(current))
            {
                merged[key] = value;
            }
        }

        var latestBackup = LatestLastBackup(
            CatalogUserAdded.CoerceString(existing.GetValueOrDefault("last_backup")),
            CatalogUserAdded.CoerceString(incoming.GetValueOrDefault("last_backup")));
        if (!string.IsNullOrWhiteSpace(latestBackup))
        {
            merged["last_backup"] = latestBackup;
        }

        return merged;
    }

    private static bool PreferRow(Dictionary<string, object?> candidate, Dictionary<string, object?> current)
    {
        var candidateHasSave = RowHasSaveLocation(candidate);
        var currentHasSave = RowHasSaveLocation(current);
        if (candidateHasSave != currentHasSave)
        {
            return candidateHasSave;
        }

        var candidateBackup = CatalogUserAdded.CoerceString(candidate.GetValueOrDefault("last_backup"));
        var currentBackup = CatalogUserAdded.CoerceString(current.GetValueOrDefault("last_backup"));
        return IsLaterBackup(candidateBackup, currentBackup);
    }

    private static bool RowHasSaveLocation(Dictionary<string, object?> row) =>
        CatalogUserAdded.CoerceBool(row.GetValueOrDefault("save_in_registry_only"))
        || !string.IsNullOrWhiteSpace(CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_path")));

    private static bool IsEmptyValue(object? value) =>
        value is null
        || value is string s && string.IsNullOrWhiteSpace(s);

    private static string? LatestLastBackup(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return IsLaterBackup(right, left) ? right : left;
    }

    private static bool IsLaterBackup(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(candidate, out var candidateTime))
        {
            return false;
        }

        return !DateTimeOffset.TryParse(current, out var currentTime)
            || candidateTime > currentTime;
    }

    /// <summary>Resolves the catalog dictionary key for a game name (exact match first, then ordinal-ignore-case).</summary>
    public bool TryGetCatalogEntryInsensitive(string gameName, out string canonicalKey, out Dictionary<string, object?> row)
    {
        lock (_lock)
        {
            RefreshIfChangedUnsafe();
            return TryFindCatalogEntryWhileLocked(gameName, out canonicalKey, out row);
        }
    }

    private bool TryFindCatalogEntryWhileLocked(string gameName, out string canonicalKey, out Dictionary<string, object?> row)
    {
        if (Catalog.TryGetValue(gameName, out row!))
        {
            canonicalKey = gameName;
            return true;
        }

        foreach (var kv in Catalog)
        {
            if (string.Equals(kv.Key, gameName, StringComparison.OrdinalIgnoreCase))
            {
                canonicalKey = kv.Key;
                row = kv.Value;
                return true;
            }
        }

        canonicalKey = string.Empty;
        row = null!;
        return false;
    }

    public void UpdateLastBackup(string gameName, string timestampIso)
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            if (!TryFindCatalogEntryWhileLocked(gameName, out _, out var row))
            {
                return;
            }

            row["last_backup"] = timestampIso;
            _dirty = true;
            PersistUnsafe();
        }
    }

    /// <summary>Removes <c>last_backup</c> from catalog rows for the given game names (case-insensitive key match).</summary>
    public void ClearLastBackupFieldsForGames(IEnumerable<string> gameNames)
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            var changed = false;
            foreach (var name in gameNames)
            {
                if (!TryFindCatalogEntryWhileLocked(name, out _, out var row))
                {
                    continue;
                }

                if (row.Remove("last_backup"))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                _dirty = true;
                PersistUnsafe();
            }
        }
    }

    /// <summary>Removes <c>last_backup</c> from every catalog row.</summary>
    public void ClearAllLastBackupFields()
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            var changed = false;
            foreach (var kv in Catalog)
            {
                if (kv.Value.Remove("last_backup"))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                _dirty = true;
                PersistUnsafe();
            }
        }
    }

    public void DeleteGames(IEnumerable<string> names)
    {
        lock (_lock)
        {
            using var processLock = AcquireCatalogLock();
            ReloadPrimaryUnsafe();
            var removed = false;
            foreach (var name in names)
            {
                removed |= Catalog.Remove(name);
            }

            if (!removed)
            {
                return;
            }

            _dirty = true;
            PersistUnsafe();
        }
    }

    public string? ResolvePath(string? path, string? gameInstallPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = PathUtils.PathToDirectoryOnly(path) ?? string.Empty;
        value = Environment.ExpandEnvironmentVariables(value);
        if (value.Contains("%INSTALLATION_PATH%", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(gameInstallPath))
        {
            value = value.Replace("%INSTALLATION_PATH%", gameInstallPath, StringComparison.OrdinalIgnoreCase);
        }

        if (value.StartsWith('~'))
        {
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[1..].TrimStart('\\', '/'));
        }

        return value;
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }

            using var processLock = AcquireCatalogLock();
            var disk = ReadPrimaryUnsafe();
            foreach (var (key, row) in disk)
            {
                if (!Catalog.ContainsKey(key))
                {
                    Catalog[key] = row;
                }
            }

            PersistUnsafe();
        }
    }

    public void RefreshFromDisk()
    {
        lock (_lock)
        {
            RefreshIfChangedUnsafe();
        }
    }

    private Dictionary<string, Dictionary<string, object?>> LoadCatalog()
    {
        if (SkipInitialDiskLoad)
        {
            return [];
        }

        var candidates = new List<string> { CatalogPath };
        if (ImportLegacyCatalogIfMissing && !string.IsNullOrWhiteSpace(LegacyCatalogPath))
        {
            candidates.Add(LegacyCatalogPath!);
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(c))
            {
                continue;
            }

            try
            {
                var parsed = ReadCatalogFile(c);
                if (!string.Equals(c, CatalogPath, StringComparison.OrdinalIgnoreCase))
                {
                    Catalog = parsed;
                    _dirty = true;
                    PersistUnsafe();
                }

                return parsed;
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    private void PersistUnsafe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        var json = JsonSerializer.Serialize(Catalog, new JsonSerializerOptions { WriteIndented = true });
        AtomicFileWrite.WriteAllText(CatalogPath, json);
        AtomicFileWrite.WriteAllText(CatalogMetadataPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            writerVersion = AppVersionInfo.RawVersion,
            updatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        }));
        _dirty = false;
        CaptureFileStampUnsafe();
    }

    private CrossProcessLock AcquireCatalogLock() =>
        CrossProcessLock.Acquire("file:" + Path.GetFullPath(CatalogPath));

    private void ReloadPrimaryUnsafe()
    {
        if (File.Exists(CatalogPath))
        {
            Catalog = ReadPrimaryUnsafe();
            CaptureFileStampUnsafe();
        }
    }

    private Dictionary<string, Dictionary<string, object?>> ReadPrimaryUnsafe()
    {
        try
        {
            return ReadCatalogFile(CatalogPath);
        }
        catch
        {
            var backup = CatalogPath + ".bak";
            return File.Exists(backup) ? ReadCatalogFile(backup) : [];
        }
    }

    private static Dictionary<string, Dictionary<string, object?>> ReadCatalogFile(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(text) ?? [];
    }

    private void RefreshIfChangedUnsafe()
    {
        try
        {
            if (!File.Exists(CatalogPath))
            {
                return;
            }

            var info = new FileInfo(CatalogPath);
            if (info.LastWriteTimeUtc != _loadedWriteUtc || info.Length != _loadedLength)
            {
                Catalog = ReadPrimaryUnsafe();
                CaptureFileStampUnsafe();
            }
        }
        catch
        {
            // Keep the last readable in-memory catalog snapshot.
        }
    }

    private void CaptureFileStampUnsafe()
    {
        if (!File.Exists(CatalogPath))
        {
            _loadedWriteUtc = DateTime.MinValue;
            _loadedLength = 0;
            return;
        }

        var info = new FileInfo(CatalogPath);
        _loadedWriteUtc = info.LastWriteTimeUtc;
        _loadedLength = info.Length;
    }
}
