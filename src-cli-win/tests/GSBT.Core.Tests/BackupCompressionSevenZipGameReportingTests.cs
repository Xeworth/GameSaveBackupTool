using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupCompressionSevenZipGameReportingTests
{
    [Fact]
    public void GetOrderedGameFoldersFromEntries_preserves_first_seen_order()
    {
        var entries = new List<(string FullPath, string EntryName)>
        {
            ("a", "Alpha/s1.dat"),
            ("b", "Beta/x/s2.dat"),
            ("c", "Alpha/s3.dat"),
            ("d", "Gamma/s4.dat"),
        };

        Assert.Equal(["Alpha", "Beta", "Gamma"], BackupCompressionService.GetOrderedGameFoldersFromEntries(entries));
    }

    [Theory]
    [InlineData("Game A/save1.dat", "Game A")]
    [InlineData("Game B/nested/save.dat", "Game B")]
    [InlineData("flat.dat", "flat.dat")]
    public void TopLevelFolderFromEntry_returns_first_path_segment(string entry, string expected) =>
        Assert.Equal(expected, BackupCompressionService.TopLevelFolderFromEntry(entry));
}
