using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>
/// Python <c>_filter_games_to_scan</c> parity: optionally skip re-scanning installed games that are already
/// catalogued with no usable save path so each run is faster and less noisy.
/// </summary>
public static class CatalogAwareDetectionFilter
{
    /// <param name="catalog">Current save catalog (same keys as <see cref="SaveCatalogManager.Catalog"/>).</param>
    /// <param name="skipWhenPreviouslyNotFound">WinUI always passes true; false is for tests / tooling parity.</param>
    public static List<GameRecord> FilterForRescan(
        IReadOnlyList<GameRecord> detected,
        IReadOnlyDictionary<string, Dictionary<string, object?>> catalog,
        bool skipWhenPreviouslyNotFound)
    {
        if (!skipWhenPreviouslyNotFound)
        {
            return [.. detected];
        }

        var list = new List<GameRecord>(detected.Count);
        foreach (var g in detected)
        {
            var key = CatalogGameKeys.FromDetectedGame(g);
            if (!catalog.TryGetValue(key, out var row))
            {
                list.Add(g);
                continue;
            }

            var raw = row.TryGetValue("save_path", out var sp) ? sp?.ToString() : null;
            var hasPath = !string.IsNullOrWhiteSpace(raw);
            var regOnly = row.TryGetValue("save_in_registry_only", out var ri) && bool.TryParse(ri?.ToString(), out var rb) && rb;
            if (hasPath || regOnly)
            {
                list.Add(g);
            }
        }

        return list;
    }
}
