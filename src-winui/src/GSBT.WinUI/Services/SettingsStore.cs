using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.WinUI.Services;

/// <summary>
/// Persists UI settings under <c>%AppData%\Roaming\GSBT\winui\winui_settings.json</c>.
/// Unpackaged WinUI apps cannot use <see cref="Windows.Storage.ApplicationData.Current"/> without package identity.
/// </summary>
public sealed class SettingsStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private Dictionary<string, JsonElement> _data;

    /// <summary>Default Roaming GSBT WinUI settings path.</summary>
    public SettingsStore()
        : this(null)
    {
    }

    /// <summary>
    /// When <paramref name="settingsDirectory"/> is set, reads/writes <c>winui_settings.json</c> in that directory only
    /// (used by the simulated main-app child process). No legacy migration runs in that mode.
    /// </summary>
    public SettingsStore(string? settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            var dir = UserDataDir.GetWinUiUserDataDir();
            _path = Path.Combine(dir, "winui_settings.json");
            MigrateFromLocalAppDataIfNeeded();
        }
        else
        {
            var d = Path.GetFullPath(settingsDirectory);
            Directory.CreateDirectory(d);
            _path = Path.Combine(d, "winui_settings.json");
        }

        _data = LoadUnsafe();
    }

    /// <summary>Full path to winui_settings.json (for diagnostics).</summary>
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

    private void MigrateFromLocalAppDataIfNeeded()
    {
        try
        {
            if (File.Exists(_path))
            {
                return;
            }

            var legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GSBT");
            var legacySettings = Path.Combine(legacyDir, "winui_settings.json");
            if (File.Exists(legacySettings))
            {
                File.Copy(legacySettings, _path, overwrite: false);
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
            return [];
        }

        try
        {
            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void PersistUnsafe()
    {
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        AtomicFileWrite.WriteAllText(_path, json);
    }
}
