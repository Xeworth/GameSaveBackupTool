using GSBT.Core.Common;
using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class GameDisplayNameTests
{
    [Theory]
    [InlineData("Battlefield™ V", "Battlefield V")]
    [InlineData("LEGO® Batman™", "LEGO Batman")]
    [InlineData("Game\u00a9Name", "GameName")]
    [InlineData("Plain", "Plain")]
    [InlineData("LEGO (TM) Star Wars", "LEGO Star Wars")]
    [InlineData("Demo (R) Title", "Demo Title")]
    public void CleanDisplayName_strips_symbols_and_collapses_space(string raw, string expected) =>
        Assert.Equal(expected, GameDisplayName.CleanDisplayName(raw));
}

public sealed class GameScanPostProcessorTests
{
    [Fact]
    public void DeduplicateBySharedSaveRoot_keeps_base_company_of_heroes_title_same_path()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coh-test-save-root"));
        var a = NewResult("1", "Company of Heroes: Tales of Valor", "20540", path);
        var b = NewResult("2", "Company of Heroes", "228200", path);
        var c = NewResult("3", "Company of Heroes: Opposing Fronts", "9340", path);

        var merged = new[] { a, b, c };
        var (kept, dropped) = GameScanPostProcessor.DeduplicateBySharedSaveRoot(merged);

        Assert.Single(kept);
        Assert.Equal("Company of Heroes", kept[0].Name);
        Assert.Equal(2, dropped.Count);
    }

    [Fact]
    public void DeduplicateBySharedSaveRoot_merges_steam_franchise_when_save_paths_overlap()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coh-nested-" + Guid.NewGuid().ToString("N")));
        var child = Path.Combine(root, "Profiles", "xyz");
        Directory.CreateDirectory(child);

        var a = NewResult("1", "Company of Heroes: Tales of Valor", "20540", child);
        var b = NewResult("2", "Company of Heroes", "228200", root);

        var (kept, dropped) = GameScanPostProcessor.DeduplicateBySharedSaveRoot(new[] { a, b });

        Assert.Single(kept);
        Assert.Equal("Company of Heroes", kept[0].Name);
        Assert.Single(dropped);
    }

    private static SaveScanResult NewResult(string rowId, string name, string appId, string resolvedPath) =>
        new()
        {
            RowId = rowId,
            Name = name,
            AppId = appId,
            InstallPath = "C:\\Steam\\steamapps\\common\\x",
            Platform = "Steam",
            SavePathRaw = resolvedPath,
            SavePathResolved = resolvedPath,
            SaveLocationDisplay = resolvedPath,
            SaveInRegistryOnly = false,
            Source = "Ludusavi",
            WallSec = 0,
            ScanOutcome = "SAVE_ON_DISK"
        };
}
