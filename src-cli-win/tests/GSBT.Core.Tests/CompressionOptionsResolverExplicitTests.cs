using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class CompressionOptionsResolverExplicitTests
{
    [Fact]
    public void FromExplicit_solid_archive_reflected_in_summary()
    {
        var perFile = CompressionOptionsResolver.FromExplicit(5, 0, solidArchive: false);
        var solid = CompressionOptionsResolver.FromExplicit(5, 0, solidArchive: true);
        Assert.False(perFile.SolidArchive);
        Assert.True(solid.SolidArchive);
        Assert.Contains("-ms=off", perFile.SummaryLabel, StringComparison.Ordinal);
        Assert.Contains("-ms=on", solid.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExplicit_uses_native_7z_engine()
    {
        var o = CompressionOptionsResolver.FromExplicit(5);
        Assert.Equal("7z", o.Engine);
        Assert.Contains("-mx=5", o.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExplicit_clamps_mx_and_auto_threads()
    {
        var o = CompressionOptionsResolver.FromExplicit(99, 0);
        Assert.Contains("-mx=9", o.SummaryLabel, StringComparison.Ordinal);
        Assert.Contains("-mmt=Auto", o.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExplicit_explicit_thread_count()
    {
        var o = CompressionOptionsResolver.FromExplicit(5, 4);
        Assert.Equal(4, o.SevenMmt);
        Assert.Contains("-mmt=4", o.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeThreadCount_zero_is_auto()
    {
        Assert.Equal(0, CompressionOptionsResolver.NormalizeThreadCount(0, 16));
    }

    [Fact]
    public void NormalizeThreadCount_clamps_to_processor_count()
    {
        Assert.Equal(8, CompressionOptionsResolver.NormalizeThreadCount(99, 8));
        Assert.Equal(0, CompressionOptionsResolver.NormalizeThreadCount(-3, 8));
    }

    [Fact]
    public void MapLegacyPresetToLevel_maps_removed_zip_presets()
    {
        Assert.Equal(0, CompressionOptionsResolver.MapLegacyPresetToLevel("store"));
        Assert.Equal(3, CompressionOptionsResolver.MapLegacyPresetToLevel("deflate_fast"));
        Assert.Equal(5, CompressionOptionsResolver.MapLegacyPresetToLevel("deflate_balanced"));
        Assert.Equal(9, CompressionOptionsResolver.MapLegacyPresetToLevel("deflate_max"));
    }
}
