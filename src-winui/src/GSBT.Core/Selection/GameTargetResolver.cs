using System.Text;
using System.Text.RegularExpressions;
using GSBT.Core.Models;

namespace GSBT.Core.Selection;

public enum GameTargetFilter
{
    Any,
    Backupable,
    Compressible,
}

public sealed class GameTargetResolution
{
    public IReadOnlyList<CatalogGameEntry> Resolved { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>Resolves CLI target tokens (indices, ranges, comma names, fuzzy names) against a list snapshot.</summary>
public static class GameTargetResolver
{
    private static readonly Regex IndexListRegex = new(@"^\d+(\s*,\s*\d+)+$", RegexOptions.Compiled);
    private static readonly Regex RangeRegex = new(@"^\d+\s*-\s*\d+$", RegexOptions.Compiled);
    private static readonly Regex SingleIndexRegex = new(@"^\d+$", RegexOptions.Compiled);

    public static GameTargetResolution Resolve(
        IReadOnlyList<CatalogGameEntry> snapshot,
        IReadOnlyList<string> rawArgs,
        GameTargetFilter filter,
        bool defaultToAllEligible)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var resolved = new List<CatalogGameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (snapshot.Count == 0)
        {
            errors.Add("Catalog is empty. Run gsbt scan first.");
            return new GameTargetResolution { Errors = errors };
        }

        var segments = ExpandTargetArgs(rawArgs);
        if (segments.Count == 0)
        {
            if (!defaultToAllEligible)
            {
                errors.Add("No targets specified. Run gsbt list, then gsbt backup 2 or gsbt backup \"Game Name\".");
                return new GameTargetResolution { Errors = errors };
            }

            foreach (var entry in snapshot)
            {
                if (!PassesFilter(entry, filter))
                {
                    continue;
                }

                AddUnique(resolved, seen, entry);
            }

            if (resolved.Count == 0)
            {
                errors.Add(filter switch
                {
                    GameTargetFilter.Backupable => "No backupable games found. Run gsbt list to see save status.",
                    GameTargetFilter.Compressible => "No compressible games found. Run gsbt backup first.",
                    _ => "No games matched the request.",
                });
            }

            return new GameTargetResolution { Resolved = resolved, Errors = errors, Warnings = warnings };
        }

        foreach (var segment in segments)
        {
            if (TryResolveIndexSegment(segment, snapshot, out var indexEntries, out var indexError))
            {
                if (indexError is not null)
                {
                    errors.Add(indexError);
                    continue;
                }

                foreach (var entry in indexEntries!)
                {
                    if (!PassesFilter(entry, filter, errors))
                    {
                        continue;
                    }

                    AddUnique(resolved, seen, entry);
                }

                continue;
            }

            var match = GameNameMatcher.Match(segment, snapshot);
            switch (match.Outcome)
            {
                case GameNameMatchOutcome.Unique:
                {
                    var entry = match.Match!;
                    if (!PassesFilter(entry, filter, errors))
                    {
                        break;
                    }

                    AddUnique(resolved, seen, entry);
                    break;
                }
                case GameNameMatchOutcome.Ambiguous:
                    errors.Add(FormatAmbiguous(segment, match.Candidates));
                    break;
                default:
                    errors.Add($"No game matches \"{segment}\". Run gsbt list.");
                    break;
            }
        }

        if (resolved.Count == 0 && errors.Count > 0 && TryResolveShellSplitFuzzyTargets(
                snapshot,
                rawArgs,
                filter,
                out var fallback))
        {
            return fallback;
        }

        return new GameTargetResolution
        {
            Resolved = resolved,
            Errors = errors,
            Warnings = warnings,
        };
    }

    public static IReadOnlyList<string> ExpandTargetArgs(IReadOnlyList<string> rawArgs)
    {
        var result = new List<string>();
        var i = 0;
        while (i < rawArgs.Count)
        {
            var arg = rawArgs[i].Trim();
            if (string.IsNullOrWhiteSpace(arg))
            {
                i++;
                continue;
            }

            if (arg.Contains(',', StringComparison.Ordinal))
            {
                result.AddRange(SplitCommaSegments(arg));
                i++;
                continue;
            }

            if (IsIndexToken(arg))
            {
                result.Add(arg);
                i++;
                continue;
            }

            var parts = new List<string> { arg };
            i++;
            var flushedByComma = false;
            while (i < rawArgs.Count)
            {
                var next = rawArgs[i].Trim();
                if (string.IsNullOrWhiteSpace(next))
                {
                    i++;
                    continue;
                }

                if (IsIndexToken(next))
                {
                    break;
                }

                if (next.Contains(',', StringComparison.Ordinal))
                {
                    parts.Add(next);
                    result.AddRange(SplitCommaSegments(string.Join(" ", parts)));
                    i++;
                    flushedByComma = true;
                    break;
                }

                parts.Add(next);
                i++;
            }

            if (!flushedByComma)
            {
                result.Add(string.Join(" ", parts));
            }
        }

        return result;
    }

    public static IReadOnlyList<string> SplitCommaSegments(string value)
    {
        var segments = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                var piece = sb.ToString().Trim();
                if (piece.Length > 0)
                {
                    segments.Add(piece);
                }

                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        var tail = sb.ToString().Trim();
        if (tail.Length > 0)
        {
            segments.Add(tail);
        }

        return segments;
    }

    private static bool IsIndexToken(string arg) =>
        SingleIndexRegex.IsMatch(arg) || RangeRegex.IsMatch(arg) || IndexListRegex.IsMatch(arg);

    private static bool TryResolveShellSplitFuzzyTargets(
        IReadOnlyList<CatalogGameEntry> snapshot,
        IReadOnlyList<string> rawArgs,
        GameTargetFilter filter,
        out GameTargetResolution resolution)
    {
        resolution = new GameTargetResolution();
        var tokens = rawArgs
            .SelectMany(arg => SplitCommaSegments(arg))
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (tokens.Count < 2 || tokens.Any(IsIndexToken))
        {
            return false;
        }

        if (!TryFindFuzzyPartition(tokens, snapshot, out var partition))
        {
            return false;
        }

        var errors = new List<string>();
        var resolved = new List<CatalogGameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in partition)
        {
            if (!PassesFilter(entry, filter, errors))
            {
                return false;
            }

            AddUnique(resolved, seen, entry);
        }

        if (resolved.Count < 2)
        {
            return false;
        }

        resolution = new GameTargetResolution
        {
            Resolved = resolved,
            Errors = errors,
            Warnings = [],
        };
        return true;
    }

    private static bool TryFindFuzzyPartition(
        IReadOnlyList<string> tokens,
        IReadOnlyList<CatalogGameEntry> snapshot,
        out IReadOnlyList<CatalogGameEntry> entries)
    {
        entries = [];
        var memo = new Dictionary<int, IReadOnlyList<CatalogGameEntry>?>();

        IReadOnlyList<CatalogGameEntry>? Search(int start)
        {
            if (start >= tokens.Count)
            {
                return [];
            }

            if (memo.TryGetValue(start, out var cached))
            {
                return cached;
            }

            for (var end = tokens.Count; end > start; end--)
            {
                var phrase = string.Join(" ", tokens.Skip(start).Take(end - start));
                var match = GameNameMatcher.Match(phrase, snapshot);
                if (match.Outcome != GameNameMatchOutcome.Unique || match.Match is null)
                {
                    continue;
                }

                var tail = Search(end);
                if (tail is null)
                {
                    continue;
                }

                var result = new List<CatalogGameEntry> { match.Match };
                result.AddRange(tail);
                memo[start] = result;
                return result;
            }

            memo[start] = null;
            return null;
        }

        var found = Search(0);
        if (found is null || found.Count == 0)
        {
            return false;
        }

        entries = found;
        return true;
    }

    private static bool TryResolveIndexSegment(
        string segment,
        IReadOnlyList<CatalogGameEntry> snapshot,
        out IReadOnlyList<CatalogGameEntry>? entries,
        out string? error)
    {
        entries = null;
        error = null;
        var max = snapshot.Count;

        if (RangeRegex.IsMatch(segment))
        {
            var parts = segment.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end))
            {
                error = $"Invalid range \"{segment}\".";
                return true;
            }

            if (start > end)
            {
                (start, end) = (end, start);
            }

            var list = new List<CatalogGameEntry>();
            for (var n = start; n <= end; n++)
            {
                if (!TryGetByIndex(snapshot, n, max, out var entry, out error))
                {
                    return true;
                }

                list.Add(entry!);
            }

            entries = list;
            return true;
        }

        if (IndexListRegex.IsMatch(segment))
        {
            var list = new List<CatalogGameEntry>();
            foreach (var part in segment.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out var index))
                {
                    return false;
                }

                if (!TryGetByIndex(snapshot, index, max, out var entry, out error))
                {
                    return true;
                }

                list.Add(entry!);
            }

            entries = list;
            return true;
        }

        if (SingleIndexRegex.IsMatch(segment))
        {
            if (!int.TryParse(segment, out var index))
            {
                return false;
            }

            if (!TryGetByIndex(snapshot, index, max, out var entry, out error))
            {
                return true;
            }

            entries = [entry!];
            return true;
        }

        return false;
    }

    private static bool TryGetByIndex(
        IReadOnlyList<CatalogGameEntry> snapshot,
        int index,
        int max,
        out CatalogGameEntry? entry,
        out string? error)
    {
        entry = null;
        error = null;
        if (index < 1 || index > max)
        {
            error = $"No row {index}. Run gsbt list (shows 1–{max}).";
            return false;
        }

        entry = snapshot.FirstOrDefault(e => e.ListIndex == index);
        if (entry is null)
        {
            error = $"No row {index}. Run gsbt list (shows 1–{max}).";
            return false;
        }

        return true;
    }

    private static bool PassesFilter(CatalogGameEntry entry, GameTargetFilter filter, IList<string>? errors = null)
    {
        switch (filter)
        {
            case GameTargetFilter.Backupable when !entry.IsBackupable:
                errors?.Add($"\"{entry.GameName}\" is not backupable: {entry.BackupSkipReason ?? "No valid save."}");
                return false;
            case GameTargetFilter.Compressible when !entry.IsCompressible:
                errors?.Add($"\"{entry.GameName}\": {entry.CompressSkipReason ?? "No backups found. Run gsbt backup first."}");
                return false;
            default:
                return true;
        }
    }

    private static void AddUnique(List<CatalogGameEntry> resolved, HashSet<string> seen, CatalogGameEntry entry)
    {
        if (seen.Add(entry.GameName))
        {
            resolved.Add(entry);
        }
    }

    private static string FormatAmbiguous(string query, IReadOnlyList<CatalogGameEntry> candidates)
    {
        var parts = candidates
            .Take(6)
            .Select(c => $"{c.ListIndex}) {c.GameName}");
        return $"\"{query}\" matches multiple games: {string.Join(", ", parts)} — use an index or full name.";
    }
}
