using System.Text.Json;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

/// <summary>
/// Catalog JSON shape parity with Python <c>game_save_data.json</c> (same keys the PyQt app persists).
/// </summary>
public sealed class CatalogRegressionTests
{
    [Fact]
    public void Minimal_catalog_round_trips_like_python_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gsbt_core_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "game_save_data.json");

        try
        {
            var mgr = new SaveCatalogManager(catalogPath: path);
            mgr.AddOrUpdate("Demo Game", new Dictionary<string, object?>
            {
                ["save_path"] = @"%TEMP%\DemoSaves",
                ["steam_app_id"] = "12345"
            });
            mgr.Flush();

            var text = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.TryGetProperty("Demo Game", out var row));
            Assert.True(row.TryGetProperty("save_path", out var sp));
            Assert.Contains("DemoSaves", sp.GetString(), StringComparison.OrdinalIgnoreCase);

            var mgr2 = new SaveCatalogManager(catalogPath: path);
            Assert.Single(mgr2.Catalog);
            Assert.True(mgr2.Catalog.ContainsKey("Demo Game"));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore test cleanup failures on locked files
            }
        }
    }

    [Fact]
    public void UserData_dir_matches_python_gsbt_layout()
    {
        var appData = UserDataDir.GetAppUserDataDir();
        Assert.Equal("GSBT", Path.GetFileName(appData.TrimEnd('\\', '/')));
    }
}
