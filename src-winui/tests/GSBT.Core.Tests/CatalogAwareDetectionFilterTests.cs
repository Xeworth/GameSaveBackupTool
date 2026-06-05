using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class CatalogAwareDetectionFilterTests
{
    [Fact]
    public void Skips_installed_row_when_catalog_has_empty_save_and_setting_on()
    {
        var steam = new GameRecord("Company", "9310", @"C:\S\steamapps\common\c", Platform: "Steam");
        var cat = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company"] = new Dictionary<string, object?>
            {
                ["save_path"] = "",
                ["steam_app_id"] = "9310"
            }
        };

        var kept = CatalogAwareDetectionFilter.FilterForRescan([steam], cat, skipWhenPreviouslyNotFound: true);
        Assert.Empty(kept);

        kept = CatalogAwareDetectionFilter.FilterForRescan([steam], cat, skipWhenPreviouslyNotFound: false);
        Assert.Single(kept);

        kept = CatalogAwareDetectionFilter.FilterForRescan([steam], new Dictionary<string, Dictionary<string, object?>>(), skipWhenPreviouslyNotFound: true);
        Assert.Single(kept);
    }

    [Fact]
    public void Keeps_installed_row_when_save_path_nonempty_or_registry_flag()
    {
        var steam = new GameRecord("CoH Test", "228200", @"C:\steam\common\c", Platform: "Steam");
        var withPath = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CoH Test"] = new Dictionary<string, object?> { ["save_path"] = @"C:\dummy\" }
        };

        Assert.Single(CatalogAwareDetectionFilter.FilterForRescan([steam], withPath, skipWhenPreviouslyNotFound: true));

        var regOnly = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CoH Test"] = new Dictionary<string, object?> { ["save_path"] = "", ["save_in_registry_only"] = true }
        };

        Assert.Single(CatalogAwareDetectionFilter.FilterForRescan([steam], regOnly, skipWhenPreviouslyNotFound: true));
    }
}
