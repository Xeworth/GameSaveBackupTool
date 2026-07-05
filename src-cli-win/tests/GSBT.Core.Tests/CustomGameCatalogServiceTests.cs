using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class CustomGameCatalogServiceTests
{
    [Fact]
    public void AddFolderGame_rejects_missing_folder()
    {
        using var dir = new TempCatalogDir();
        var mgr = new SaveCatalogManager(dir.CatalogPath);

        var (ok, message) = CustomGameCatalogService.AddFolderGame(
            mgr,
            "Test Game",
            @"C:\this\path\should\not\exist\gsbt-test");

        Assert.False(ok);
        Assert.Contains("exist", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddFolderGame_adds_custom_entry()
    {
        using var dir = new TempCatalogDir();
        var saveDir = dir.CreateSubdir("saves");
        var mgr = new SaveCatalogManager(dir.CatalogPath);

        var (ok, message) = CustomGameCatalogService.AddFolderGame(
            mgr,
            "My Custom Game",
            saveDir);

        Assert.True(ok);
        Assert.Contains("My Custom Game", message, StringComparison.Ordinal);
        Assert.True(mgr.Catalog.ContainsKey("My Custom Game"));
        Assert.Equal("Custom", mgr.Catalog["My Custom Game"]["platform"]);
    }

    private sealed class TempCatalogDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "gsbt-test-" + Guid.NewGuid().ToString("N"));

        public string CatalogPath => Path.Combine(Root, "game_save_data.json");

        public string CreateSubdir(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
