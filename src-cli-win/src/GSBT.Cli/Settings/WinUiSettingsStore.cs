using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.Cli.Settings;

/// <summary>Read/write view of WinUI <c>winui_settings.json</c> (same file as GSBT.WinUI).</summary>
public sealed class WinUiSettingsStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private Dictionary<string, JsonElement> _data;

    public WinUiSettingsStore(string? settingsDirectory = null)
    {
        var dir = string.IsNullOrWhiteSpace(settingsDirectory)
            ? UserDataDir.GetWinUiUserDataDir()
            : Path.GetFullPath(settingsDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "winui_settings.json");
        MigrateFromLegacyIfNeeded();
        _data = LoadUnsafe();
    }

    public string SettingsFilePath => _path;

    public bool ContainsKey(string key)
    {
        lock (_lock)
        {
            return _data.ContainsKey(key);
        }
    }

    public T Get<T>(string key, T fallback)
    {
        lock (_lock)
        {
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
            _data[key] = JsonSerializer.SerializeToElement(value);
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
    {
        var def = Get("default_backup_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(def))
        {
            return def.Trim();
        }

        var last = Get("last_backup_path", string.Empty);
        return string.IsNullOrWhiteSpace(last) ? null : last.Trim();
    }

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
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private void PersistUnsafe()
    {
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        AtomicFileWrite.WriteAllText(_path, json);
    }
}
