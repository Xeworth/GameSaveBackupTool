namespace GSBT.Core.Models;

/// <summary>Result of matching a game title + optional Steam app id against the compiled Ludusavi manifest.</summary>
public sealed record LudusaviSaveLookup(
    IReadOnlyList<string> Paths,
    LudusaviMatchKind MatchKind);
