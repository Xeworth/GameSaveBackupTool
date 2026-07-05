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
    public void Suggested_default_backup_path_uses_gsbt_backups_folder()
    {
        Assert.Equal("gsbt-backups", BackupPaths.SuggestedFolderName);
        var path = BackupPaths.SuggestedDefaultBackupPath();
        Assert.EndsWith("gsbt-backups", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void UserData_dir_uses_full_app_folder_name()
    {
        var appData = UserDataDir.GetAppUserDataDir();
        Assert.Equal(UserDataDir.AppFolderName, Path.GetFileName(appData.TrimEnd('\\', '/')));
    }

    [Fact]
    public void WinUi_user_data_dir_lives_under_app_root()
    {
        var root = UserDataDir.GetAppUserDataDir();
        var winUi = UserDataDir.GetWinUiUserDataDir();
        Assert.Equal(Path.Combine(root, UserDataDir.WinUiSubdir), winUi.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_short_data_dir_uses_gsbt_subfolder()
    {
        var shortRoot = UserDataDir.GetLocalShortDataDir();
        Assert.Equal(UserDataDir.ShortSubdirName, Path.GetFileName(shortRoot.TrimEnd('\\', '/')));
        Assert.EndsWith(UserDataDir.AppFolderName, Path.GetDirectoryName(shortRoot.TrimEnd('\\', '/'))!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roaming_legacy_gsbt_folder_migrates_into_app_root()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "gsbt_migrate_" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(baseDir, UserDataDir.LegacyAppFolderName);
        var target = Path.Combine(baseDir, UserDataDir.AppFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "migration_marker.txt"), "legacy");

        try
        {
            UserDataDir.MigrateLegacyDirectoryForTests(legacy, target);
            Assert.True(File.Exists(Path.Combine(target, "migration_marker.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore test cleanup failures on locked files
            }
        }
    }
}
