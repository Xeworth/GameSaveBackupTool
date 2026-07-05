using System.Text.Json;
using System.Text.RegularExpressions;
using GSBT.Core.Common;
using GSBT.Core.Models;
using YamlDotNet.Serialization;

namespace GSBT.Core.Services;

public sealed class LudusaviManifestProvider
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
    private const string ManifestFilename = "ludusavi-save-manifest.json";
    private const string MetaFilename = "ludusavi-save-manifest.meta.json";
    private const int MaxManifestDownloadBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan ManifestHttpTimeout = TimeSpan.FromMinutes(3);
    private static readonly Regex NameClean = new("[^a-z0-9]+", RegexOptions.Compiled);

    private readonly string _dataDir;
    private readonly string _manifestPath;
    private readonly string _metaPath;
    private readonly string? _bundledManifestPath;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();
    private JsonElement? _cache;

    public LudusaviManifestProvider(string? dataDir = null, string? bundledManifestPath = null, HttpClient? httpClient = null)
    {
        _dataDir = dataDir ?? UserDataDir.GetWinUiUserDataDir();
        _manifestPath = Path.Combine(_dataDir, ManifestFilename);
        _metaPath = Path.Combine(_dataDir, MetaFilename);
        _bundledManifestPath = bundledManifestPath;
        _httpClient = httpClient ?? new HttpClient { Timeout = ManifestHttpTimeout };
    }

    public static string NormalizeManifestGameName(string name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : NameClean.Replace(name.Trim().ToLowerInvariant(), " ").Trim();

    public JsonElement LoadManifestOfflineOnly()
    {
        lock (_lock)
        {
            if (_cache is { } ready)
            {
                return ready;
            }

            var doc = LoadManifestDocumentFromDisk() ?? SeedManifestFromBundle();
            if (doc is null)
            {
                doc = CreateEmptyManifest();
            }

            _cache = doc.Value;
            return _cache.Value;
        }
    }

    public async Task<string> RefreshNowAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // keep sync behavior around shared state
        }

        var req = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        var meta = LoadMeta();
        if (meta.TryGetValue("etag", out var etag) && !string.IsNullOrWhiteSpace(etag))
        {
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.SendAsync(req, ct);
        }
        catch
        {
            return "network_error";
        }

        if ((int)resp.StatusCode == 304)
        {
            var current = LoadManifestDocumentFromDisk();
            if (current is null)
            {
                return "not_modified_without_cache";
            }

            meta["fetched_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            SaveMeta(meta);
            lock (_lock)
            {
                _cache = current.Value;
            }

            return "not_modified";
        }

        if (!resp.IsSuccessStatusCode)
        {
            return $"http_{(int)resp.StatusCode}";
        }

        string yaml;
        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var sb = new System.Text.StringBuilder();
            var buffer = new char[8192];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                if (sb.Length + read > MaxManifestDownloadBytes)
                {
                    return "manifest_too_large";
                }

                sb.Append(buffer, 0, read);
            }

            yaml = sb.ToString();
        }
        catch
        {
            return "network_error";
        }

        JsonElement compiled;
        try
        {
            compiled = CompileYamlToCompactManifest(yaml);
        }
        catch
        {
            return "yaml_error";
        }

        SaveManifest(compiled);
        var newMeta = new Dictionary<string, string>
        {
            ["fetched_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["etag"] = resp.Headers.ETag?.Tag ?? string.Empty
        };
        SaveMeta(newMeta);

        lock (_lock)
        {
            _cache = compiled;
        }

        return "updated";
    }

    /// <param name="strictSteamIndexing">
    /// When true and <paramref name="steamAppId"/> is set: if the manifest's <c>steam_index</c> contains that id,
    /// use only those paths (no <c>name_index</c> fallback). If that id is <b>not</b> listed in <c>steam_index</c> at all,
    /// fall back to <c>name_index</c> so titles Ludusavi only maps by name still resolve (e.g. some older bundles).
    /// When the id is listed with an empty path list, keep that as "no manifest paths" (no name fallback).
    /// This avoids wrong-title matches when the manifest documents a Steam id that points at a different classic release,
    /// without dropping games that were never given a <c>steam:</c> block for that id.
    /// </param>
    public IReadOnlyList<string> FindSavePaths(string gameName, string? steamAppId, bool strictSteamIndexing = false)
        => FindSavePathsWithMeta(gameName, steamAppId, strictSteamIndexing).Paths;

    /// <inheritdoc cref="FindSavePaths(string, string?, bool)"/>
    /// <returns>Paths plus whether they came from <see cref="LudusaviMatchKind.SteamId"/> or <see cref="LudusaviMatchKind.NameIndex"/>.</returns>
    public LudusaviSaveLookup FindSavePathsWithMeta(string gameName, string? steamAppId, bool strictSteamIndexing = false)
    {
        var manifest = LoadManifestOfflineOnly();
        var appKey = (steamAppId ?? string.Empty).Trim();

        if (manifest.TryGetProperty("steam_index", out var steamIndex) &&
            !string.IsNullOrWhiteSpace(appKey) &&
            steamIndex.TryGetProperty(appKey, out var steamEntryForApp))
        {
            if (steamEntryForApp.ValueKind == JsonValueKind.Array)
            {
                var fromSteam = steamEntryForApp.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                return new LudusaviSaveLookup(fromSteam, LudusaviMatchKind.SteamId);
            }

            // steam_index lists this app id but not as a path array — do not guess from title.
            if (strictSteamIndexing)
            {
                return new LudusaviSaveLookup([], LudusaviMatchKind.SteamId);
            }
        }

        var norm = NormalizeManifestGameName(gameName);
        if (manifest.TryGetProperty("name_index", out var nameIdx) &&
            nameIdx.TryGetProperty(norm, out var namePaths) &&
            namePaths.ValueKind == JsonValueKind.Array)
        {
            var fromName = namePaths.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return new LudusaviSaveLookup(fromName, LudusaviMatchKind.NameIndex);
        }

        return new LudusaviSaveLookup([], LudusaviMatchKind.None);
    }

    private static JsonElement CompileYamlToCompactManifest(string yaml)
    {
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<object, object?>>(yaml);

        var nameIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var steamIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalGames = 0;

        foreach (var (k, v) in root)
        {
            var gameName = k?.ToString() ?? string.Empty;
            if (v is not Dictionary<object, object?> entry)
            {
                continue;
            }

            totalGames++;

            if (entry.TryGetValue("alias", out var aliasObj) && aliasObj is string alias && !string.IsNullOrWhiteSpace(alias))
            {
                aliases[gameName] = alias.Trim();
                continue;
            }

            if (!entry.TryGetValue("files", out var filesObj) || filesObj is not Dictionary<object, object?> files)
            {
                continue;
            }

            var savePaths = new List<string>();
            foreach (var (fp, fm) in files)
            {
                var path = fp?.ToString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (fm is not Dictionary<object, object?> fileMeta || !IsWindowsSaveEntry(fileMeta))
                {
                    continue;
                }

                var translated = TranslateManifestPath(path.Trim());
                if (!savePaths.Contains(translated, StringComparer.OrdinalIgnoreCase))
                {
                    savePaths.Add(translated);
                }
            }

            if (savePaths.Count == 0)
            {
                continue;
            }

            var normName = NormalizeManifestGameName(gameName);
            if (!string.IsNullOrWhiteSpace(normName))
            {
                nameIndex[normName] = [.. savePaths];
            }

            if (entry.TryGetValue("steam", out var steamObj) && steamObj is Dictionary<object, object?> steamDict &&
                steamDict.TryGetValue("id", out var sidObj))
            {
                var sid = sidObj?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(sid) && sid.All(char.IsDigit))
                {
                    steamIndex[sid] = [.. savePaths];
                }
            }
        }

        foreach (var (fromName, toName) in aliases)
        {
            var src = NormalizeManifestGameName(fromName);
            var dst = NormalizeManifestGameName(toName);
            if (nameIndex.TryGetValue(dst, out var paths))
            {
                nameIndex[src] = [.. paths];
            }
        }

        var payload = new
        {
            version = 1,
            generated_at_unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            source_url = ManifestUrl,
            stats = new
            {
                games_total_in_yaml = totalGames,
                games_with_windows_save_paths = nameIndex.Count,
                steam_ids_indexed = steamIndex.Count
            },
            name_index = nameIndex,
            steam_index = steamIndex
        };

        return JsonSerializer.SerializeToElement(payload);
    }

    private static bool IsWindowsSaveEntry(Dictionary<object, object?> fileMeta)
    {
        if (!fileMeta.TryGetValue("tags", out var tagsObj) || tagsObj is not IEnumerable<object> tags)
        {
            return false;
        }

        if (!tags.Select(x => x?.ToString()?.Trim().ToLowerInvariant()).Contains("save"))
        {
            return false;
        }

        if (!fileMeta.TryGetValue("when", out var whenObj) || whenObj is null)
        {
            return true;
        }

        if (whenObj is IEnumerable<object> list)
        {
            foreach (var item in list)
            {
                if (item is not Dictionary<object, object?> cond)
                {
                    continue;
                }

                if (!cond.TryGetValue("os", out var osObj))
                {
                    return true;
                }

                var os = osObj?.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(os) || os == "windows")
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static string TranslateManifestPath(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<home>"] = "~",
            ["<winAppData>"] = "%APPDATA%",
            ["<winLocalAppData>"] = "%LOCALAPPDATA%",
            ["<winLocalAppDataLow>"] = "%USERPROFILE%\\AppData\\LocalLow",
            ["<winDocuments>"] = "%USERPROFILE%\\Documents",
            ["<winPublic>"] = "%PUBLIC%",
            ["<winProgramData>"] = "%PROGRAMDATA%",
            ["<winDir>"] = "%WINDIR%",
            ["<root>"] = "%INSTALLATION_PATH%",
            ["<base>"] = "%INSTALLATION_PATH%",
            ["<storeUserId>"] = "<user-id>"
        };

        var output = path;
        foreach (var (from, to) in map)
        {
            output = output.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }

        return output.Replace('/', '\\');
    }

    private JsonElement? SeedManifestFromBundle()
    {
        var path = _bundledManifestPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var raw = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            SaveManifest(raw);
            SaveMeta(new Dictionary<string, string>
            {
                ["fetched_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ["etag"] = string.Empty
            });
            return raw;
        }
        catch
        {
            return null;
        }
    }

    private JsonElement? LoadManifestDocumentFromDisk()
    {
        if (!File.Exists(_manifestPath))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(_manifestPath)).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement CreateEmptyManifest() => JsonSerializer.SerializeToElement(new
    {
        version = 1,
        generated_at_unix = 0,
        source_url = ManifestUrl,
        stats = new { },
        name_index = new Dictionary<string, string[]>(),
        steam_index = new Dictionary<string, string[]>()
    });

    private Dictionary<string, string> LoadMeta()
    {
        if (!File.Exists(_metaPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_metaPath))
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveMeta(Dictionary<string, string> meta)
    {
        Directory.CreateDirectory(_dataDir);
        AtomicFileWrite.WriteAllText(_metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveManifest(JsonElement manifest)
    {
        Directory.CreateDirectory(_dataDir);
        AtomicFileWrite.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
