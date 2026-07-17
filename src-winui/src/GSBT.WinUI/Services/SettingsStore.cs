using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.WinUI.Services;

/// <summary>
/// Persists UI settings under <c>%AppData%\Game Save Backup Tool\winui\winui_settings.json</c>.
/// Unpackaged WinUI apps cannot use <see cref="Windows.Storage.ApplicationData.Current"/> without package identity.
/// </summary>
public sealed class SettingsStore
{
    private readonly object _lock = new();
    private readonly string _path;
    private Dictionary<string, JsonElement> _data;
    private DateTime _loadedWriteUtc;
    private long _loadedLength;

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
        CaptureFileStampUnsafe();
    }

    /// <summary>Full path to winui_settings.json (for diagnostics).</summary>
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

    private void MigrateFromLocalAppDataIfNeeded()
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
            return [];
        }

        try
        {
            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? [];
        }
        catch
        {
            try
            {
                var backup = _path + ".bak";
                return File.Exists(backup)
                    ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(backup)) ?? []
                    : [];
            }
            catch
            {
                return [];
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
