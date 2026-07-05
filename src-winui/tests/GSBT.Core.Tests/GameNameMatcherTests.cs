using GSBT.Core.Models;
using GSBT.Core.Selection;

namespace GSBT.Core.Tests;

public sealed class GameNameMatcherTests
{
    private static IReadOnlyList<CatalogGameEntry> Sample() =>
    [
        Entry(1, "Elden Ring"),
        Entry(2, "Elden Ring DLC"),
        Entry(3, "Hades"),
        Entry(4, "Call of Duty 4"),
    ];

    private static CatalogGameEntry Entry(int index, string name) => new()
    {
        ListIndex = index,
        GameName = name,
        Platform = "Steam",
        SaveStatusLabel = "Found",
        IsBackupable = true,
        IsCompressible = false,
        LastBackupDisplay = "Not yet",
    };

    [Fact]
    public void Match_ExactName_IsUnique()
    {
        var result = GameNameMatcher.Match("Hades", Sample());
        Assert.Equal(GameNameMatchOutcome.Unique, result.Outcome);
        Assert.Equal("Hades", result.Match!.GameName);
    }

    [Fact]
    public void Match_PartialUnique_StartsWith()
    {
        var result = GameNameMatcher.Match("hades", Sample());
        Assert.Equal(GameNameMatchOutcome.Unique, result.Outcome);
        Assert.Equal("Hades", result.Match!.GameName);
    }

    [Fact]
    public void Match_AmbiguousElden_ReturnsMultiple()
    {
        var result = GameNameMatcher.Match("elden", Sample());
        Assert.Equal(GameNameMatchOutcome.Ambiguous, result.Outcome);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Match_None_ReturnsNone()
    {
        var result = GameNameMatcher.Match("zzz", Sample());
        Assert.Equal(GameNameMatchOutcome.None, result.Outcome);
    }
}
