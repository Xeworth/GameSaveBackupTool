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
    private const int MaxManifestGames = 100_000;
    private const int MaxPathsPerGame = 128;
    private const int MaxManifestPaths = 500_000;
    private const int MaxManifestPathLength = 4096;
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

    public ManifestProvenance GetProvenance()
    {
        var manifest = LoadManifestOfflineOnly();
        var valid = ValidateCompiledManifest(manifest, out var validationError);
        var meta = LoadMeta();
        var source = meta.TryGetValue("source", out var recordedSource) && !string.IsNullOrWhiteSpace(recordedSource)
            ? recordedSource
            : meta.TryGetValue("etag", out var etag) && !string.IsNullOrWhiteSpace(etag)
                ? "downloaded"
                : File.Exists(_manifestPath)
                    ? "bundled-or-legacy-cache"
                    : "empty";
        var version = manifest.TryGetProperty("version", out var versionElement)
            ? versionElement.ToString()
            : "unknown";
        var generatedAtUtc = TryReadUnixTimestamp(manifest, "generated_at_unix");
        var fetchedAtUtc = meta.TryGetValue("fetched_at_unix", out var fetched)
            && long.TryParse(fetched, out var fetchedUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(fetchedUnix)
                : (DateTimeOffset?)null;
        var sourceUrl = manifest.TryGetProperty("source_url", out var urlElement)
            ? urlElement.GetString()
            : null;
        var sanitizedPathsRemoved = meta.TryGetValue("sanitized_paths_removed", out var removedText)
            && int.TryParse(removedText, out var removed)
                ? removed
                : 0;

        return new ManifestProvenance(
            source,
            version,
            generatedAtUtc,
            fetchedAtUtc,
            valid,
            valid ? "validated" : validationError ?? "validation failed",
            sourceUrl,
            sanitizedPathsRemoved);
    }

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

        using var req = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        var meta = LoadMeta();
        if (meta.TryGetValue("etag", out var etag) && !string.IsNullOrWhiteSpace(etag))
        {
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        HttpResponseMessage? response;
        try
        {
            response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch
        {
            return "network_error";
        }

        using var resp = response;

        if ((int)resp.StatusCode == 304)
        {
            var current = LoadManifestDocumentFromDisk();
            if (current is null)
            {
                return "not_modified_without_cache";
            }

            meta = LoadMeta();
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
            if (!ValidateCompiledManifest(compiled, out _))
            {
                return "manifest_invalid";
            }
        }
        catch
        {
            return "yaml_error";
        }

        SaveManifest(compiled);
        var newMeta = new Dictionary<string, string>
        {
            ["fetched_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["etag"] = resp.Headers.ETag?.Tag ?? string.Empty,
            ["source"] = "downloaded",
            ["sanitized_paths_removed"] = "0",
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
        var totalPaths = 0;

        foreach (var (k, v) in root)
        {
            var gameName = k?.ToString() ?? string.Empty;
            if (v is not Dictionary<object, object?> entry)
            {
                continue;
            }

            totalGames++;
            if (totalGames > MaxManifestGames)
            {
                throw new InvalidDataException($"Manifest contains more than {MaxManifestGames} games.");
            }

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
                if (!IsSafeManifestPathTemplate(translated))
                {
                    continue;
                }

                if (!savePaths.Contains(translated, StringComparer.OrdinalIgnoreCase))
                {
                    if (savePaths.Count >= MaxPathsPerGame || totalPaths >= MaxManifestPaths)
                    {
                        throw new InvalidDataException("Manifest path limits were exceeded.");
                    }

                    savePaths.Add(translated);
                    totalPaths++;
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

    internal static bool IsSafeManifestPathTemplate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxManifestPathLength || path.IndexOf('\0') >= 0)
        {
            return false;
        }

        var normalized = path.Trim().Replace('/', '\\');
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment == ".."))
        {
            return false;
        }

        string[] unsafeRoots =
        [
            "~",
            "%USERPROFILE%",
            "%APPDATA%",
            "%LOCALAPPDATA%",
            "%PROGRAMDATA%",
            "%PUBLIC%",
            "%WINDIR%",
            "%INSTALLATION_PATH%",
        ];
        if (unsafeRoots.Contains(normalized.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("%WINDIR%", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.IsPathRooted(normalized)
            && string.Equals(
                Path.GetPathRoot(normalized)?.TrimEnd('\\'),
                normalized.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateCompiledManifest(JsonElement manifest, out string? error)
    {
        error = null;
        if (manifest.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("name_index", out var names)
            || names.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("steam_index", out var steam)
            || steam.ValueKind != JsonValueKind.Object)
        {
            error = "Manifest indexes are missing or malformed.";
            return false;
        }

        var games = 0;
        var paths = 0;
        foreach (var index in new[] { names, steam })
        {
            foreach (var property in index.EnumerateObject())
            {
                games++;
                if (games > MaxManifestGames * 2 || property.Value.ValueKind != JsonValueKind.Array)
                {
                    error = "Manifest index limits or shape are invalid.";
                    return false;
                }

                var perGame = 0;
                foreach (var value in property.Value.EnumerateArray())
                {
                    perGame++;
                    paths++;
                    if (value.ValueKind != JsonValueKind.String
                        || !IsSafeManifestPathTemplate(value.GetString() ?? string.Empty)
                        || perGame > MaxPathsPerGame
                        || paths > MaxManifestPaths * 2)
                    {
                        error = "Manifest contains an unsafe path or exceeds path limits.";
                        return false;
                    }
                }
            }
        }

        return true;
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
            if (!TryPrepareManifest(raw, out var prepared, out var removed))
            {
                return null;
            }

            SaveManifest(prepared);
            SaveMeta(new Dictionary<string, string>
            {
                ["fetched_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ["etag"] = string.Empty,
                ["source"] = "bundled",
                ["sanitized_paths_removed"] = removed.ToString(),
            });
            return prepared;
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
            var manifest = JsonDocument.Parse(File.ReadAllText(_manifestPath)).RootElement.Clone();
            if (!TryPrepareManifest(manifest, out var prepared, out var removed))
            {
                return null;
            }

            if (removed > 0)
            {
                SaveManifest(prepared);
                var meta = LoadMeta();
                meta["sanitized_paths_removed"] = removed.ToString();
                SaveMeta(meta);
            }

            return prepared;
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

    private static bool TryPrepareManifest(
        JsonElement manifest,
        out JsonElement prepared,
        out int removedPaths)
    {
        prepared = default;
        removedPaths = 0;
        if (ValidateCompiledManifest(manifest, out _))
        {
            prepared = manifest;
            return true;
        }

        if (manifest.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("name_index", out var names)
            || names.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("steam_index", out var steam)
            || steam.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var sanitizedNames = SanitizeIndex(names, ref removedPaths);
        var sanitizedSteam = SanitizeIndex(steam, ref removedPaths);
        if (sanitizedNames is null || sanitizedSteam is null)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["version"] = manifest.TryGetProperty("version", out var version) ? version.Clone() : 1,
            ["generated_at_unix"] = manifest.TryGetProperty("generated_at_unix", out var generated) ? generated.Clone() : 0,
            ["source_url"] = manifest.TryGetProperty("source_url", out var sourceUrl) ? sourceUrl.Clone() : ManifestUrl,
            ["stats"] = manifest.TryGetProperty("stats", out var stats) ? stats.Clone() : new { },
            ["name_index"] = sanitizedNames,
            ["steam_index"] = sanitizedSteam,
        };
        prepared = JsonSerializer.SerializeToElement(payload);
        return removedPaths > 0 && ValidateCompiledManifest(prepared, out _);
    }

    private static Dictionary<string, string[]>? SanitizeIndex(JsonElement index, ref int removedPaths)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var entries = 0;
        var paths = 0;
        foreach (var property in index.EnumerateObject())
        {
            entries++;
            if (entries > MaxManifestGames * 2 || property.Value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var safe = property.Value.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString() ?? string.Empty)
                .Where(IsSafeManifestPathTemplate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxPathsPerGame + 1)
                .ToArray();
            var originalCount = property.Value.GetArrayLength();
            removedPaths += originalCount - safe.Length;
            paths += safe.Length;
            if (safe.Length > MaxPathsPerGame || paths > MaxManifestPaths * 2)
            {
                return null;
            }

            if (safe.Length > 0)
            {
                result[property.Name] = safe;
            }
        }

        return result;
    }

    private static DateTimeOffset? TryReadUnixTimestamp(JsonElement manifest, string propertyName)
    {
        if (!manifest.TryGetProperty(propertyName, out var value)
            || !value.TryGetInt64(out var unix)
            || unix <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

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
        if (!ValidateCompiledManifest(manifest, out var error))
        {
            throw new InvalidDataException(error ?? "Manifest validation failed.");
        }

        Directory.CreateDirectory(_dataDir);
        AtomicFileWrite.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record ManifestProvenance(
    string Source,
    string Version,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset? FetchedAtUtc,
    bool IsValid,
    string ValidationStatus,
    string? SourceUrl,
    int SanitizedPathsRemoved);
