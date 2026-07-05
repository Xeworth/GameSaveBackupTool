using GSBT.Core.Catalog;

namespace GSBT.Core.Tests;

public sealed class GameCatalogFilterTests
{
    [Fact]
    public void FoundOnly_excludes_missing_paths_and_is_not_confused_by_not_found_text()
    {
        Assert.False(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.FoundOnly, hasSaveLocation: false));
        Assert.True(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.FoundOnly, hasSaveLocation: true));
        Assert.True(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.All, hasSaveLocation: false));

        // Regression: UI must not use string.Contains("Found") — "✗ Not Found" contains the substring "Found".
        const string buggyUiLabel = "\u2717 Not Found";
        Assert.Contains("Found", buggyUiLabel, StringComparison.Ordinal);
        Assert.False(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.FoundOnly,
            GameCatalogFilter.HasSaveLocation(null, registryOnly: false)));
    }

    [Fact]
    public void Registry_only_counts_as_found()
    {
        Assert.True(GameCatalogFilter.HasSaveLocation(null, registryOnly: true));
        Assert.True(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.FoundOnly,
            GameCatalogFilter.HasSaveLocation(null, registryOnly: true)));
    }

    [Fact]
    public void NotFoundOnly_shows_only_rows_without_save()
    {
        Assert.True(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.NotFoundOnly,
            GameCatalogFilter.HasSaveLocation(null, registryOnly: false)));
        Assert.False(GameCatalogFilter.IncludeRow(GameCatalogFilterMode.NotFoundOnly,
            GameCatalogFilter.HasSaveLocation("C:\\\\temp", registryOnly: false)));
    }
}
