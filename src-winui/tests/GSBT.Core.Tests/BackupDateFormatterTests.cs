using System.Globalization;
using GSBT.Core.Common;

namespace GSBT.Core.Tests;

public sealed class BackupDateFormatterTests
{
    [Fact]
    public void FormatDisplay_uses_system_format_by_default()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var actual = BackupDateFormatter.FormatDisplay("2026-07-04T20:01:00Z", null);

            Assert.Contains("/", actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatDisplay_preserves_iso_key()
    {
        var actual = BackupDateFormatter.FormatDisplay("2026-07-04T20:01:00Z", "iso");

        Assert.StartsWith("2026-07-04 |", actual);
    }
}
