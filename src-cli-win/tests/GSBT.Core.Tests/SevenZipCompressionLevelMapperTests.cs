using GSBT.Core.Services;
using SharpSevenZip;

namespace GSBT.Core.Tests;

public sealed class SevenZipCompressionLevelMapperTests
{
    [Fact]
    public void SupportedMxLevels_are_only_real_7zip_tiers()
    {
        Assert.Equal([0, 1, 3, 5, 7, 9], SevenZipCompressionLevelMapper.SupportedMxLevels);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(8, 9)]
    [InlineData(9, 9)]
    public void NormalizeMx_snaps_legacy_values_to_supported_tiers(int input, int expected) =>
        Assert.Equal(expected, SevenZipCompressionLevelMapper.NormalizeMx(input));

    [Theory]
    [InlineData(0, 0, CompressionLevel.None)]
    [InlineData(1, 1, CompressionLevel.Fast)]
    [InlineData(2, 3, CompressionLevel.Low)]
    [InlineData(3, 5, CompressionLevel.Normal)]
    [InlineData(4, 7, CompressionLevel.High)]
    [InlineData(5, 9, CompressionLevel.Ultra)]
    public void Slider_index_round_trips_mx_and_enum(int index, int mx, CompressionLevel level)
    {
        Assert.Equal(mx, SevenZipCompressionLevelMapper.MxFromSliderIndex(index));
        Assert.Equal(index, SevenZipCompressionLevelMapper.SliderIndexFromMx(mx));
        Assert.Equal(level, SevenZipCompressionLevelMapper.MapMxToCompressionLevel(mx));
    }
}
