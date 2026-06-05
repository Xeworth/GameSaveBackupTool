using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class CompressionOptionsResolverExplicitTests
{
    [Fact]
    public void FromExplicit_store_uses_zipfile_engine()
    {
        var o = CompressionOptionsResolver.FromExplicit("store", "7z", 9, 8, string.Empty);
        Assert.Equal("zipfile", o.Engine);
    }

    [Fact]
    public void FromExplicit_seven_zip_summary_contains_mx()
    {
        var o = CompressionOptionsResolver.FromExplicit("seven_zip", "7z", 3, 0, string.Empty);
        Assert.Equal("7z", o.Engine);
        Assert.Contains("-mx=3", o.SummaryLabel, StringComparison.Ordinal);
    }
}
