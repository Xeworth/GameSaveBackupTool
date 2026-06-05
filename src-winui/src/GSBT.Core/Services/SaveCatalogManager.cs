using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.Core.Services;

public sealed class SaveCatalogManager
{
    private readonly object _lock = new();
    private bool _dirty;

    public string CatalogPath { get; }
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
            var appData = UserDataDir.GetAppUserDataDir();
            CatalogPath = Path.Combine(appData, "game_save_data.json");
            LegacyCatalogPath = legacyCatalogPath ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "game_save_data.json"));
        }

        Catalog = LoadCatalog();
    }

    /// <summary>When true, <see cref="LoadCatalog"/> did not read disk (fresh session until first persist).</summary>
    public bool SkipInitialDiskLoad { get; }

    public bool ImportLegacyCatalogIfMissing { get; }

    public void AddOrUpdate(string gameName, Dictionary<string, object?> payload)
    {
        lock (_lock)
        {
            Catalog[gameName] = payload;
            _dirty = true;
        }
    }

    /// <summary>Resolves the catalog dictionary key for a game name (exact match first, then ordinal-ignore-case).</summary>
    public bool TryGetCatalogEntryInsensitive(string gameName, out string canonicalKey, out Dictionary<string, object?> row)
    {
        lock (_lock)
        {
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

            PersistUnsafe();
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
                var text = File.ReadAllText(c);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(text) ?? [];
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
        _dirty = false;
    }
}
