using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.Core.Tests;

public sealed class CatalogUserAddedTests
{
    [Fact]
    public void Explicit_flag_is_user_added()
    {
        var row = new Dictionary<string, object?>
        {
            [CatalogUserAdded.JsonPropertyName] = true,
            ["steam_app_id"] = "123"
        };
        Assert.True(CatalogUserAdded.IsUserAddedEntry(row));
    }

    [Fact]
    public void Legacy_row_without_steam_id_and_save_on_disk_is_user_added()
    {
        var row = new Dictionary<string, object?>
        {
            ["scan_outcome"] = "SAVE_ON_DISK",
            ["save_path"] = @"C:\Saves\MyGame"
        };
        Assert.True(CatalogUserAdded.IsUserAddedEntry(row));
    }

    [Fact]
    public void Scanner_row_with_steam_id_is_not_user_added()
    {
        var row = new Dictionary<string, object?>
        {
            ["steam_app_id"] = "",
            ["scan_outcome"] = "SAVE_ON_DISK",
            ["save_path"] = @"C:\Saves"
        };
        Assert.False(CatalogUserAdded.IsUserAddedEntry(row));
    }

    [Fact]
    public void Json_deserialized_bool_element_is_recognized()
    {
        var json = """{"is_custom_game":true,"save_path":"C:\\\\x"}""";
        var row = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        Assert.True(CatalogUserAdded.IsUserAddedEntry(row));
    }
}
