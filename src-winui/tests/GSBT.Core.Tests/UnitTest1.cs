using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public class UnitTest1
{
    [Fact]
    public void PathToDirectoryOnly_Strips_FilePatternSuffix()
    {
        var input = @"%LOCALAPPDATA%\Foo\SaveGame*.foo";
        var output = PathUtils.PathToDirectoryOnly(input);
        Assert.Equal(@"%LOCALAPPDATA%\Foo\", output);
    }

    [Fact]
    public void SaveCatalogManager_Stores_And_Flushes_Data()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"gsbt-core-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var path = Path.Combine(tmp, "game_save_data.json");
        var mgr = new SaveCatalogManager(path);
        mgr.AddOrUpdate("Test Game", new Dictionary<string, object?> { ["save_path"] = @"%APPDATA%\Foo\" });
        mgr.Flush();
        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        Assert.Contains("Test Game", json);
    }

    [Fact]
    public void ManifestProvider_Loads_Bundled_Offline()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var bundled = Path.Combine(root, "src", "GSBT.WinUI", "data", "ludusavi-save-manifest.json");
        Assert.True(File.Exists(bundled));

        var tmp = Path.Combine(Path.GetTempPath(), $"gsbt-manifest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var provider = new LudusaviManifestProvider(tmp, bundled);
        var manifest = provider.LoadManifestOfflineOnly();
        Assert.True(manifest.TryGetProperty("name_index", out _));
        Assert.True(manifest.TryGetProperty("steam_index", out _));
    }
}