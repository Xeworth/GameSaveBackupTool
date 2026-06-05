using GSBT.Core.Models;
using System.Globalization;

namespace GSBT.Core.Services;

/// <summary>
/// Post-scan merge: multiple Steam library rows that resolve to the same save data (exact path, or nested folders under the same franchise title).
/// </summary>
public static class GameScanPostProcessor
{
    /// <summary>
    /// Merges duplicate save rows: same resolved folder, or (Steam only) same franchise prefix in the title with overlapping save paths on disk.
    /// </summary>
    public static (IReadOnlyList<SaveScanResult> Kept, IReadOnlyList<string> DroppedCatalogNames) DeduplicateBySharedSaveRoot(
        IReadOnlyList<SaveScanResult> results)
    {
        if (results.Count == 0)
        {
            return (results, []);
        }

        var n = results.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var ki = NormalizeDiskSaveKey(results[i]);
                var kj = NormalizeDiskSaveKey(results[j]);
                if (ki is not null && kj is not null && string.Equals(ki, kj, StringComparison.OrdinalIgnoreCase))
                {
                    Union(i, j);
                    continue;
                }

                if (ki is null || kj is null)
                {
                    continue;
                }

                if (!IsSteam(results[i]) || !IsSteam(results[j]))
                {
                    continue;
                }

                if (!string.Equals(FranchiseTitle(results[i]), FranchiseTitle(results[j]), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PathsOverlap(results[i].SavePathResolved!, results[j].SavePathResolved!))
                {
                    Union(i, j);
                }
            }
        }

        var byRoot = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var r = Find(i);
            if (!byRoot.TryGetValue(r, out var list))
            {
                list = [];
                byRoot[r] = list;
            }

            list.Add(i);
        }

        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var winnerRowIdByRoot = new Dictionary<int, string>();
        foreach (var kv in byRoot)
        {
            var indices = kv.Value;
            var members = indices.Select(ix => results[ix]).ToList();
            var w = PickPreferredSameSaveRoot(members);
            winnerRowIdByRoot[kv.Key] = w.RowId;
            foreach (var ix in indices)
            {
                if (!string.Equals(results[ix].RowId, w.RowId, StringComparison.OrdinalIgnoreCase))
                {
                    dropped.Add(results[ix].Name);
                }
            }
        }

        var kept = new List<SaveScanResult>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (string.Equals(results[i].RowId, winnerRowIdByRoot[root], StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(results[i]);
            }
        }

        return (kept, dropped.ToList());
    }

    private static string? NormalizeDiskSaveKey(SaveScanResult r)
    {
        if (string.IsNullOrWhiteSpace(r.SavePathResolved))
        {
            return null;
        }

        if (r.SaveInRegistryOnly)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(r.SavePathResolved).TrimEnd('\\', '/').ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSteam(SaveScanResult r) =>
        string.Equals(r.Platform, "Steam", StringComparison.OrdinalIgnoreCase);

    /// <summary>First segment before ':' (Relic-style expansions), otherwise full name.</summary>
    private static string FranchiseTitle(SaveScanResult r)
    {
        var n = r.Name;
        var c = n.IndexOf(':');
        return (c < 0 ? n : n[..c]).Trim();
    }

    private static bool PathsOverlap(string a, string b)
    {
        try
        {
            var fa = Path.GetFullPath(a).TrimEnd('\\', '/');
            var fb = Path.GetFullPath(b).TrimEnd('\\', '/');
            if (string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return fa.StartsWith(fb + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fa.StartsWith(fb + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fb.StartsWith(fa + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fb.StartsWith(fa + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static SaveScanResult PickPreferredSameSaveRoot(IReadOnlyList<SaveScanResult> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        foreach (var candidate in group.OrderBy(x => x.Name.Length).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var n = candidate.Name;
            if (group.All(o =>
                    string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase)
                    || o.Name.StartsWith(n + ":", StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        SaveScanResult? best = null;
        var bestId = long.MaxValue;
        foreach (var o in group)
        {
            if (!long.TryParse((o.AppId ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (id < bestId)
            {
                bestId = id;
                best = o;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).First();
    }
}
