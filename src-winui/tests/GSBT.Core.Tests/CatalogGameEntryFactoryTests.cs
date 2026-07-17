using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class CatalogGameEntryFactoryTests
{
    [Fact]
    public void BuildSortedList_NormalizesRawTrademarkOnlyDetectedKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt-test-" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(root, "LEGO Batman Saves");
        Directory.CreateDirectory(save);

        try
        {
            var catalog = new SaveCatalogManager(
                catalogPath: Path.Combine(root, "game_save_data.json"),
                skipInitialDiskLoad: true);
            catalog.AddOrUpdate("LEGO® Batman™: The Videogame", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["save_path"] = save,
            });

            var rows = CatalogGameEntryFactory.BuildSortedList(
                catalog,
                backupRoot: null,
                subfolderPerGame: true,
                deduplicateSharedSaveFolders: true);

            var row = Assert.Single(rows);
            Assert.Equal("LEGO Batman: The Videogame", row.GameName);
            Assert.True(catalog.Catalog.ContainsKey("LEGO Batman: The Videogame"));
            Assert.False(catalog.Catalog.ContainsKey("LEGO® Batman™: The Videogame"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildSortedList_MergesTrademarkDuplicateAndPreservesLatestBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt-test-" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(root, "LEGO Batman Saves");
        Directory.CreateDirectory(save);

        try
        {
            var catalog = new SaveCatalogManager(
                catalogPath: Path.Combine(root, "game_save_data.json"),
                skipInitialDiskLoad: true);
            catalog.AddOrUpdate("LEGO Batman: The Videogame", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["last_backup"] = "2026-07-04T12:00:00Z",
            });
            catalog.AddOrUpdate("LEGO® Batman™: The Videogame", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["save_path"] = save,
                ["last_backup"] = "2026-07-04T20:01:00Z",
            });

            var rows = CatalogGameEntryFactory.BuildSortedList(
                catalog,
                backupRoot: null,
                subfolderPerGame: true,
                deduplicateSharedSaveFolders: true,
                dateFormat: "iso");

            var row = Assert.Single(rows);
            Assert.Equal("LEGO Batman: The Videogame", row.GameName);
            Assert.True(row.IsBackupable);
            Assert.Equal("2026-07-04T20:01:00Z", row.LastBackupIso);
            Assert.Single(catalog.Catalog);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildSortedList_DeduplicatesTrademarkVariantWithSameSaveFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt-test-" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(root, "LEGO Batman Saves");
        Directory.CreateDirectory(save);

        try
        {
            var catalog = new SaveCatalogManager(
                catalogPath: Path.Combine(root, "game_save_data.json"),
                skipInitialDiskLoad: true);
            catalog.AddOrUpdate("LEGO Batman: The Videogame", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["save_path"] = save,
            });
            catalog.AddOrUpdate("LEGO® Batman™: The Videogame", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["save_path"] = save,
            });

            var rows = CatalogGameEntryFactory.BuildSortedList(
                catalog,
                backupRoot: null,
                subfolderPerGame: true,
                deduplicateSharedSaveFolders: true);

            var row = Assert.Single(rows);
            Assert.Equal(1, row.ListIndex);
            Assert.Equal("LEGO Batman: The Videogame", row.GameName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildSortedList_KeepsUserAddedRowsOutOfSharedSaveDedupe()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt-test-" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(root, "Shared Saves");
        Directory.CreateDirectory(save);

        try
        {
            var catalog = new SaveCatalogManager(
                catalogPath: Path.Combine(root, "game_save_data.json"),
                skipInitialDiskLoad: true);
            catalog.AddOrUpdate("Detected Game", new Dictionary<string, object?>
            {
                ["platform"] = "Steam",
                ["save_path"] = save,
            });
            catalog.AddOrUpdate("My Custom Alias", new Dictionary<string, object?>
            {
                ["platform"] = "Custom",
                ["save_path"] = save,
                ["is_custom_game"] = true,
            });

            var rows = CatalogGameEntryFactory.BuildSortedList(
                catalog,
                backupRoot: null,
                subfolderPerGame: true,
                deduplicateSharedSaveFolders: true);

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.GameName == "Detected Game");
            Assert.Contains(rows, r => r.GameName == "My Custom Alias");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
