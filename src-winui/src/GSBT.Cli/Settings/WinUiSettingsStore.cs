using System.Text.Json;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Cli.Settings;

/// <summary>Read/write view of WinUI <c>winui_settings.json</c> (same file as GSBT.WinUI).</summary>
public sealed class WinUiSettingsStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private Dictionary<string, JsonElement> _data;
    private DateTime _loadedWriteUtc;
    private long _loadedLength;

    public WinUiSettingsStore(string? settingsDirectory = null)
    {
        var dir = string.IsNullOrWhiteSpace(settingsDirectory)
            ? UserDataDir.GetWinUiUserDataDir()
            : Path.GetFullPath(settingsDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "winui_settings.json");
        MigrateFromLegacyIfNeeded();
        _data = LoadUnsafe();
        CaptureFileStampUnsafe();
    }

    public string SettingsFilePath => _path;

    public bool ContainsKey(string key)
    {
        lock (_lock)
        {
            RefreshIfChangedUnsafe();
            return _data.ContainsKey(key);
        }
    }

    public T Get<T>(string key, T fallback)
    {
        lock (_lock)
        {
            RefreshIfChangedUnsafe();
            if (!_data.TryGetValue(key, out var el))
            {
                return fallback;
            }

            try
            {
                return el.Deserialize<T>()!;
            }
            catch
            {
                return fallback;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            using var processLock = CrossProcessLock.Acquire("file:" + Path.GetFullPath(_path));
            _data = LoadUnsafe();
            _data[key] = JsonSerializer.SerializeToElement(value);
            _data["_schema_version"] = JsonSerializer.SerializeToElement(1);
            PersistUnsafe();
        }
    }

    public string? ResolveBackupDestination()
    {
        foreach (var candidate in new[]
                 {
                     Get("default_backup_path", string.Empty),
                     Get("last_backup_path", string.Empty),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch
            {
                // try next
            }
        }

        return null;
    }

    public bool HasPersistedDefaultBackupPath()
    {
        return ContainsKey("default_backup_path")
            && !string.IsNullOrWhiteSpace(Get("default_backup_path", string.Empty));
    }

    public string? GetBackupPathSuggestion()
        => BackupDestinationPolicy.GetSuggestion(Get);

    private void MigrateFromLegacyIfNeeded()
    {
        try
        {
            if (File.Exists(_path))
            {
                return;
            }

            var legacySettingsPaths = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    UserDataDir.LegacyAppFolderName,
                    "winui_settings.json"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    UserDataDir.LegacyAppFolderName,
                    UserDataDir.WinUiSubdir,
                    "winui_settings.json"),
            };

            foreach (var legacySettings in legacySettingsPaths)
            {
                if (File.Exists(legacySettings))
                {
                    File.Copy(legacySettings, _path, overwrite: false);
                    break;
                }
            }
        }
        catch
        {
            // best-effort migration
        }
    }

    private Dictionary<string, JsonElement> LoadUnsafe()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch
        {
            try
            {
                var backup = _path + ".bak";
                return File.Exists(backup)
                    ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(backup))
                        ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
        }
    }

    private void PersistUnsafe()
    {
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        AtomicFileWrite.WriteAllText(_path, json);
        CaptureFileStampUnsafe();
    }

    private void RefreshIfChangedUnsafe()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var info = new FileInfo(_path);
            if (info.LastWriteTimeUtc != _loadedWriteUtc || info.Length != _loadedLength)
            {
                _data = LoadUnsafe();
                CaptureFileStampUnsafe();
            }
        }
        catch
        {
            // Keep the last readable in-memory settings snapshot.
        }
    }

    private void CaptureFileStampUnsafe()
    {
        if (!File.Exists(_path))
        {
            _loadedWriteUtc = DateTime.MinValue;
            _loadedLength = 0;
            return;
        }

        var info = new FileInfo(_path);
        _loadedWriteUtc = info.LastWriteTimeUtc;
        _loadedLength = info.Length;
    }
}
