using GSBT.Core.Models;
using GSBT.Core.Selection;

namespace GSBT.Core.Tests;

public sealed class GameTargetResolverTests
{
    private static IReadOnlyList<CatalogGameEntry> Sample() =>
    [
        Entry(1, "Alpha"),
        Entry(2, "Bravo"),
        Entry(3, "Charlie"),
        Entry(4, "Delta"),
        Entry(5, "Echo"),
    ];

    private static CatalogGameEntry Entry(int index, string name, bool backupable = true) => new()
    {
        ListIndex = index,
        GameName = name,
        Platform = "Steam",
        SaveStatusLabel = "Found",
        IsBackupable = backupable,
        IsCompressible = backupable,
        LastBackupDisplay = "Not yet",
    };

    [Fact]
    public void Resolve_DefaultAllBackupable_ReturnsAll()
    {
        var result = GameTargetResolver.Resolve(Sample(), [], GameTargetFilter.Backupable, defaultToAllEligible: true);
        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Resolved.Count);
    }

    [Fact]
    public void Resolve_SingleIndex_Works()
    {
        var result = GameTargetResolver.Resolve(Sample(), ["3"], GameTargetFilter.Backupable, defaultToAllEligible: false);
        Assert.Single(result.Resolved);
        Assert.Equal("Charlie", result.Resolved[0].GameName);
    }

    [Fact]
    public void Resolve_IndexList_Works()
    {
        var result = GameTargetResolver.Resolve(Sample(), ["1,3,5"], GameTargetFilter.Backupable, defaultToAllEligible: false);
        Assert.Equal(3, result.Resolved.Count);
    }

    [Fact]
    public void Resolve_Range_Works()
    {
        var result = GameTargetResolver.Resolve(Sample(), ["2-4"], GameTargetFilter.Backupable, defaultToAllEligible: false);
        Assert.Equal(3, result.Resolved.Count);
        Assert.Equal("Bravo", result.Resolved[0].GameName);
        Assert.Equal("Delta", result.Resolved[2].GameName);
    }

    [Fact]
    public void Resolve_OutOfRange_AddsError()
    {
        var result = GameTargetResolver.Resolve(Sample(), ["9"], GameTargetFilter.Backupable, defaultToAllEligible: false);
        Assert.Contains(result.Errors, e => e.Contains("No row 9"));
    }

    [Fact]
    public void ExpandTargetArgs_JoinsMultiWordName()
    {
        var segments = GameTargetResolver.ExpandTargetArgs(["elden", "ring"]);
        Assert.Single(segments);
        Assert.Equal("elden ring", segments[0]);
    }

    [Fact]
    public void SplitCommaSegments_RespectsQuotes()
    {
        var parts = GameTargetResolver.SplitCommaSegments("a, \"b, c\", d");
        Assert.Equal(3, parts.Count);
        Assert.Equal("a", parts[0]);
        Assert.Equal("b, c", parts[1]);
        Assert.Equal("d", parts[2]);
    }

    [Fact]
    public void ExpandTargetArgs_SplitsCommaAcrossShellTokens()
    {
        var segments = GameTargetResolver.ExpandTargetArgs(
            ["mafia", "class,", "mafia", "def,", "lego", "star", "wars"]);
        Assert.Equal(3, segments.Count);
        Assert.Equal("mafia class", segments[0]);
        Assert.Equal("mafia def", segments[1]);
        Assert.Equal("lego star wars", segments[2]);
    }

    [Fact]
    public void ExpandTargetArgs_SingleArgCommaList()
    {
        var segments = GameTargetResolver.ExpandTargetArgs(["mafia class, mafia def, lego star wars"]);
        Assert.Equal(3, segments.Count);
    }

    [Fact]
    public void Resolve_ShellSplitFuzzyTargets_WorksWhenCommasWereStripped()
    {
        var entries = new[]
        {
            Entry(1, "Hozy"),
            Entry(2, "Sons Of The Forest"),
            Entry(3, "Trepang2"),
        };

        var result = GameTargetResolver.Resolve(
            entries,
            ["trep", "ho", "sons"],
            GameTargetFilter.Backupable,
            defaultToAllEligible: false);

        Assert.Empty(result.Errors);
        Assert.Equal(["Trepang2", "Hozy", "Sons Of The Forest"], result.Resolved.Select(r => r.GameName));
    }

    [Fact]
    public void Resolve_ShellSplitFuzzyTargets_PartitionsMultiWordNames()
    {
        var entries = new[]
        {
            Entry(1, "LEGO Batman: The Videogame"),
            Entry(2, "LEGO Star Wars - The Complete Saga"),
            Entry(3, "Mafia II: Definitive Edition"),
            Entry(4, "Trepang2"),
        };

        var result = GameTargetResolver.Resolve(
            entries,
            ["trep", "lego", "star", "mafia", "def"],
            GameTargetFilter.Backupable,
            defaultToAllEligible: false);

        Assert.Empty(result.Errors);
        Assert.Equal(
            ["Trepang2", "LEGO Star Wars - The Complete Saga", "Mafia II: Definitive Edition"],
            result.Resolved.Select(r => r.GameName));
    }

    [Fact]
    public void Resolve_ShellSplitFuzzyTargets_PartitionsAdjacentMultiWordNames()
    {
        var entries = new[]
        {
            Entry(1, "LEGO Batman: The Videogame"),
            Entry(2, "LEGO Star Wars - The Complete Saga"),
            Entry(3, "Mafia II: Definitive Edition"),
        };

        var result = GameTargetResolver.Resolve(
            entries,
            ["lego", "star", "lego", "batman", "mafia", "def"],
            GameTargetFilter.Backupable,
            defaultToAllEligible: false);

        Assert.Empty(result.Errors);
        Assert.Equal(
            ["LEGO Star Wars - The Complete Saga", "LEGO Batman: The Videogame", "Mafia II: Definitive Edition"],
            result.Resolved.Select(r => r.GameName));
    }

    [Fact]
    public void Resolve_MultiWordFuzzyName_StillWorksAsOneTarget()
    {
        var entries = new[]
        {
            Entry(1, "Sons Of The Forest"),
            Entry(2, "Trepang2"),
        };

        var result = GameTargetResolver.Resolve(
            entries,
            ["sons", "of", "the", "forest"],
            GameTargetFilter.Backupable,
            defaultToAllEligible: false);

        Assert.Empty(result.Errors);
        Assert.Single(result.Resolved);
        Assert.Equal("Sons Of The Forest", result.Resolved[0].GameName);
    }
}
