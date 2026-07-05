using GSBT.Core.Models;

namespace GSBT.Core.Selection;

public enum GameNameMatchOutcome
{
    None,
    Unique,
    Ambiguous,
}

public sealed class GameNameMatchResult
{
    public GameNameMatchOutcome Outcome { get; init; }

    public CatalogGameEntry? Match { get; init; }

    public IReadOnlyList<CatalogGameEntry> Candidates { get; init; } = [];
}

/// <summary>Fuzzy game name resolution against a numbered catalog snapshot.</summary>
public static class GameNameMatcher
{
    public static GameNameMatchResult Match(string query, IReadOnlyList<CatalogGameEntry> snapshot)
    {
        var q = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(q) || snapshot.Count == 0)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.None };
        }

        var exact = snapshot
            .Where(e => string.Equals(e.GameName, q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.Unique, Match = exact[0] };
        }

        if (exact.Count > 1)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.Ambiguous, Candidates = exact };
        }

        var ranked = RankCandidates(q, snapshot);
        if (ranked.Count == 0)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.None };
        }

        if (ranked.Count == 1)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.Unique, Match = ranked[0] };
        }

        var topScore = Score(q, ranked[0].GameName);
        var tied = ranked.Where(e => Score(q, e.GameName) == topScore).ToList();
        if (tied.Count == 1)
        {
            return new GameNameMatchResult { Outcome = GameNameMatchOutcome.Unique, Match = tied[0] };
        }

        return new GameNameMatchResult { Outcome = GameNameMatchOutcome.Ambiguous, Candidates = tied };
    }

    private static List<CatalogGameEntry> RankCandidates(string query, IReadOnlyList<CatalogGameEntry> snapshot)
    {
        var q = query.Trim();
        var qLower = q.ToLowerInvariant();
        var tokens = Tokenize(qLower);

        var hits = new List<(CatalogGameEntry Entry, int Score)>();
        foreach (var entry in snapshot)
        {
            var score = Score(q, entry.GameName);
            if (score > 0)
            {
                hits.Add((entry, score));
            }
            else if (tokens.Length > 0 && AllTokensPresent(tokens, entry.GameName))
            {
                hits.Add((entry, 10));
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Entry.GameName, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Entry)
            .ToList();
    }

    private static int Score(string query, string name)
    {
        var q = query.Trim();
        if (string.Equals(name, q, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        return 0;
    }

    private static string[] Tokenize(string query) =>
        query.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool AllTokensPresent(string[] tokens, string name)
    {
        foreach (var token in tokens)
        {
            if (!name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
