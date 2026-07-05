using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class GamePlatformHeuristicsTests
{
    [Theory]
    [InlineData("GOG.com", @"C:\Games\Witcher 3", null, "GOG")]
    [InlineData("CD Projekt RED", @"C:\GOG Galaxy\Games\Witcher 3", null, "GOG")]
    [InlineData(null, @"D:\Games\Example", @"C:\Program Files\GOG Galaxy\GalaxyClient.exe goggame-1207658924", "GOG")]
    [InlineData("Valve", @"C:\Steam\steamapps\common\Half-Life", null, "Steam")]
    [InlineData("Epic Games", @"C:\Epic Games\Fortnite", null, "Epic")]
    [InlineData("Some Studio", @"D:\Games\Indie Title", null, "Other")]
    public void DetectFromRegistry_maps_store_signals(string? publisher, string install, string? uninstall, string expected) =>
        Assert.Equal(expected, GamePlatformHeuristics.DetectFromRegistry(null, publisher, install, uninstall));

    [Fact]
    public void InferFromInstallPath_returns_gog_for_galaxy_folder() =>
        Assert.Equal("GOG", GamePlatformHeuristics.InferFromInstallPath(@"C:\GOG Galaxy\Games\Cyberpunk 2077"));

    [Fact]
    public void InferFromInstallPath_returns_null_for_generic_pc_path() =>
        Assert.Null(GamePlatformHeuristics.InferFromInstallPath(@"D:\Games\Generic"));

    [Theory]
    [InlineData(null, "Other")]
    [InlineData("", "Other")]
    [InlineData("Unknown", "Other")]
    [InlineData("PC", "Other")]
    [InlineData("Steam", "Steam")]
    [InlineData("GOG", "GOG")]
    [InlineData("Custom", "Custom")]
    public void FormatForDisplay_maps_non_store_to_other(string? platform, string expected) =>
        Assert.Equal(expected, GamePlatformHeuristics.FormatForDisplay(platform));
}
