using System.Collections.Concurrent;
using System.Linq;
using GSBT.Core.Common;
using GSBT.Core.Models;
using System.Runtime.Versioning;

namespace GSBT.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class ScanService
{
    private readonly IGameDetector _gameDetector;
    private readonly SaveCatalogManager _saveCatalogManager;
    private readonly LudusaviManifestProvider _manifestProvider;
    private readonly RegistrySaveResolver _registryResolver;

    public ScanService(
        IGameDetector gameDetector,
        SaveCatalogManager saveCatalogManager,
        LudusaviManifestProvider manifestProvider,
        RegistrySaveResolver registryResolver)
    {
        _gameDetector = gameDetector;
        _saveCatalogManager = saveCatalogManager;
        _manifestProvider = manifestProvider;
        _registryResolver = registryResolver;
    }

    public void EnsureManifestLoadedOffline() => _manifestProvider.LoadManifestOfflineOnly();

    public Task<string> RefreshManifestOnlineAsync(CancellationToken cancellationToken = default)
        => _manifestProvider.RefreshNowAsync(cancellationToken);

    public async Task<IReadOnlyList<GameRecord>> DetectGamesAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _gameDetector.DetectAllGamesAsync(cancellationToken);
        return raw;
    }

    /// <summary>
    /// Resolves save locations in parallel, then optionally merges rows that share the same on-disk save folder (DLC duplicates).
    /// <paramref name="onProgressTick"/> is invoked once per detected game after resolution (for UI progress), before merge.
    /// </summary>
    /// <param name="deduplicateSharedSaveFolders">When true (default), collapse same-save-root titles into one row and drop extras from the catalog.</param>
    public async Task RunSaveFetchParallelAsync(
        IReadOnlyList<GameRecord> games,
        IReadOnlyDictionary<string, string> steamIds,
        Action<SaveScanResult> onEach,
        Action<string>? trace = null,
        Action? onProgressTick = null,
        int maxWorkers = 6,
        bool deduplicateSharedSaveFolders = true,
        CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        var deduped = games
            .GroupBy(RowId)
            .Select(g => g.First())
            .ToList();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(maxWorkers, Math.Max(1, deduped.Count)),
            CancellationToken = cancellationToken
        };

        var bag = new ConcurrentBag<SaveScanResult>();
        await Parallel.ForEachAsync(deduped, options, (game, ct) =>
        {
            var result = ProcessSingleGame(game, steamIds, trace);
            bag.Add(result);
            onProgressTick?.Invoke();
            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        });

        var merged = bag.ToList();
        if (deduplicateSharedSaveFolders)
        {
            var (kept, droppedNames) = GameScanPostProcessor.DeduplicateBySharedSaveRoot(merged);
            if (droppedNames.Count > 0)
            {
                _saveCatalogManager.DeleteGames(droppedNames);
            }

            foreach (var r in kept)
            {
                PersistCatalogForScanResult(r);
                onEach(r);
            }
        }
        else
        {
            foreach (var r in merged.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                PersistCatalogForScanResult(r);
                onEach(r);
            }
        }

        _saveCatalogManager.Flush();
    }

    private void PersistCatalogForScanResult(SaveScanResult result)
    {
        var hasExisting = _saveCatalogManager.TryGetCatalogEntryInsensitive(result.Name, out var canonicalKey, out var existingRow);

        if (hasExisting && CatalogUserAdded.IsUserAddedEntry(existingRow))
        {
            return;
        }

        var saveData = BuildCatalogPayload(result);
        if (hasExisting)
        {
            MergeLastBackupFromExisting(existingRow, saveData);
        }

        var catalogKey = hasExisting ? canonicalKey : result.Name;
        _saveCatalogManager.AddOrUpdate(catalogKey, saveData);
    }

    private static void MergeLastBackupFromExisting(Dictionary<string, object?> existingRow, Dictionary<string, object?> saveData)
    {
        var lb = CatalogUserAdded.CoerceString(existingRow.GetValueOrDefault("last_backup"));
        if (!string.IsNullOrWhiteSpace(lb))
        {
            saveData["last_backup"] = lb;
        }
    }

    private static Dictionary<string, object?> BuildCatalogPayload(SaveScanResult result)
    {
        var saveData = new Dictionary<string, object?>
        {
            ["steam_app_id"] = result.AppId,
            ["scan_outcome"] = result.ScanOutcome
        };

        if (!string.IsNullOrWhiteSpace(result.Platform))
        {
            saveData["platform"] = result.Platform;
        }

        var resolvedViaRegistry = string.Equals(result.Source, "Ludusavi (registry)", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(result.SavePathRaw))
        {
            saveData["save_path"] = result.SavePathRaw!;
            if (resolvedViaRegistry)
            {
                saveData["save_resolved_via_registry"] = true;
            }
        }
        else if (result.SaveInRegistryOnly && result.SaveRegistryHive is not null && result.SaveRegistrySubkey is not null)
        {
            saveData["save_path"] = string.Empty;
            saveData["save_registry_hive"] = result.SaveRegistryHive;
            saveData["save_registry_subkey"] = result.SaveRegistrySubkey;
            saveData["save_in_registry_only"] = true;
        }
        else
        {
            saveData["save_path"] = string.Empty;
        }

        return saveData;
    }

    private SaveScanResult ProcessSingleGame(GameRecord game, IReadOnlyDictionary<string, string> steamIds, Action<string>? trace)
    {
        var t0 = DateTimeOffset.UtcNow;
        var gameShort = game.Name.Length > 42 ? game.Name[..42] : game.Name;
        void Tr(string msg)
        {
            trace?.Invoke($"{DateTime.Now:HH:mm:ss.fff} | {gameShort} | {msg}");
        }

        Tr($"BEGIN manifest lookup app_id={game.AppId ?? "null"}");

        var strictSteamIndexing = string.Equals(game.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(game.AppId);
        var manifestLookup = _manifestProvider.FindSavePathsWithMeta(game.Name, game.AppId, strictSteamIndexing);
        var candidates = manifestLookup.Paths;
        // Wrong-title Ludusavi rows often come from name_index when this Steam id is missing from steam_index.
        // Avoid mining registry hints from those unrelated path strings; user JSON can still map the app id explicitly.
        var suppressManifestHintsFromNameOnlyMatch =
            strictSteamIndexing
            && manifestLookup.MatchKind == LudusaviMatchKind.NameIndex
            && !string.IsNullOrWhiteSpace(game.AppId);

        string? finalRawPath = null;
        var sourceType = "Not Found";
        var resolvedViaRegistry = false;
        var registryOnly = false;
        string? regHive = null;
        string? regSub = null;
        var hints = new List<string>();

        foreach (var candidate in candidates)
        {
            if (candidate.Contains("<user-id>", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in new[] { "steamid64", "steamid3" })
                {
                    var id = steamIds.TryGetValue(key, out var sid) ? sid : string.Empty;
                    var test = candidate.Replace("<user-id>", id, StringComparison.OrdinalIgnoreCase);
                    var resolved = _saveCatalogManager.ResolvePath(test, game.InstallPath);
                    if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
                    {
                        finalRawPath = test;
                        break;
                    }
                }
            }
            else
            {
                var resolved = _saveCatalogManager.ResolvePath(candidate, game.InstallPath);
                if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
                {
                    finalRawPath = candidate;
                    break;
                }
            }

            if (!suppressManifestHintsFromNameOnlyMatch)
            {
                hints.AddRange(RegistrySaveResolver.ExtractRegistryHints(candidate));
            }
        }

        var appId = (game.AppId ?? string.Empty).Trim();
        if (_registryResolver.MergedSteamRegistrySaveKeys.TryGetValue(appId, out var pair))
        {
            var fullHint = $"{pair.Hive}\\{pair.Subkey}";
            if (!hints.Contains(fullHint, StringComparer.OrdinalIgnoreCase))
            {
                hints.Insert(0, fullHint);
            }
        }

        if (string.IsNullOrWhiteSpace(finalRawPath))
        {
            foreach (var hint in hints.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var folder = _registryResolver.ResolveRegistryHintToSaveFolder(hint);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    finalRawPath = folder;
                    resolvedViaRegistry = true;
                    sourceType = "Ludusavi (registry)";
                    break;
                }

                var inKey = _registryResolver.TryRegistryKeyAsInKeySaveLocation(hint);
                if (inKey is not null)
                {
                    registryOnly = true;
                    regHive = inKey.Value.Hive;
                    regSub = inKey.Value.Subkey;
                    sourceType = "Registry (in-key save data)";
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(finalRawPath) && !resolvedViaRegistry)
        {
            sourceType = "Ludusavi";
        }

        var resolvedPath = !string.IsNullOrWhiteSpace(finalRawPath)
            ? _saveCatalogManager.ResolvePath(finalRawPath, game.InstallPath)
            : null;
        var display = resolvedPath ?? (registryOnly && regHive is not null && regSub is not null ? RegistrySaveResolver.FormatRegistrySaveDisplay(regHive, regSub) : null);

        var outcome = !string.IsNullOrWhiteSpace(finalRawPath)
            ? "SAVE_ON_DISK"
            : registryOnly
                ? "REGISTRY_IN_KEY"
                : candidates.Count > 0
                    ? "MANIFEST_PATHS_NO_DISK"
                    : "NO_MANIFEST_PATHS";

        return new SaveScanResult
        {
            RowId = RowId(game),
            Name = game.CatalogDisplayName ?? GameDisplayName.CleanDisplayName(game.Name),
            AppId = game.AppId,
            InstallPath = game.InstallPath,
            Platform = game.Platform,
            SavePathRaw = finalRawPath,
            SavePathResolved = resolvedPath,
            SaveLocationDisplay = display,
            SaveInRegistryOnly = registryOnly,
            SaveRegistryHive = regHive,
            SaveRegistrySubkey = regSub,
            Source = sourceType,
            WallSec = Math.Round((DateTimeOffset.UtcNow - t0).TotalSeconds, 3),
            ScanOutcome = outcome
        };
    }

    private static string RowId(GameRecord g)
        => $"{g.Name}\u001E{g.AppId ?? string.Empty}\u001E{(g.InstallPath ?? string.Empty).ToLowerInvariant()}";
}
