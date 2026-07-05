using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupCompressionCollectTests
{
    [Theory]
    [InlineData("Backups_2026-01-01_01-01-01.zip", true)]
    [InlineData("Backups_2026-01-01_01-01-01.7z", true)]
    [InlineData("BACKUPS_2026-01-01_01-01-01.ZIP", true)]
    [InlineData("games/Backups_2026-01-01_01-01-01.zip", false)]
    [InlineData("Game1/save.bin", false)]
    [InlineData("readme.txt", false)]
    public void IsRootGsbtBackupArchiveRelativeEntry_matches_expected(string relative, bool expected) =>
        Assert.Equal(expected, BackupCompressionService.IsRootGsbtBackupArchiveRelativeEntry(relative));

    [Fact]
    public void CollectRelativeEntries_skips_root_level_backups_archives_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_collect_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MyGame"));
            File.WriteAllText(Path.Combine(root, "MyGame", "a.txt"), "save");
            File.WriteAllText(Path.Combine(root, "Backups_2020-01-01_12-00-00.zip"), "oldarchive");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "oops.txt"), "x");

            var (entries, _, count) = BackupCompressionService.CollectRelativeEntries(root);
            Assert.Equal(2, count);
            var names = entries.Select(e => e.EntryName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("MyGame/a.txt", names);
            Assert.Contains("nested/oops.txt", names);
            Assert.DoesNotContain("Backups_2020-01-01_12-00-00.zip", names);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore temp cleanup races
            }
        }
    }

    [Fact]
    public void CollectRelativeEntries_filters_to_selected_game_subfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_filter_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "GameA"));
            Directory.CreateDirectory(Path.Combine(root, "GameB"));
            File.WriteAllText(Path.Combine(root, "GameA", "save.dat"), "a");
            File.WriteAllText(Path.Combine(root, "GameB", "save.dat"), "b");

            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GameA" };
            var (entries, _, count) = BackupCompressionService.CollectRelativeEntries(root, subfolderPerGame: true, filter);
            Assert.Equal(1, count);
            Assert.Single(entries);
            Assert.Equal("GameA/save.dat", entries[0].EntryName.Replace('\\', '/'));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Theory]
    [InlineData("GameA - Backup 2026-01-01/file.txt", "GameA", true)]
    [InlineData("GameB - Backup 2026-01-01/file.txt", "GameA", false)]
    public void EntryMatchesGameFilter_flat_layout_prefix(string entry, string safe, bool expected)
    {
        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { safe };
        Assert.Equal(
            expected,
            BackupCompressionService.EntryMatchesGameFilter(entry, subfolderPerGame: false, filter));
    }
}
