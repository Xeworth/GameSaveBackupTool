using System.Globalization;

namespace GSBT.WinUI.Services;

/// <summary>Live clock strings for the compression screen saver (respects <c>date_format</c> setting).</summary>
internal static class ScreenSaverClockFormatter
{
    public static (string TimeLine, string DateLine) FormatNow(string? dateFormatKey)
    {
        var now = DateTime.Now;
        var key = (dateFormatKey ?? "iso").Trim().ToLowerInvariant();
        var month = now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        return key switch
        {
            "us" => (
                $"{now.ToString("tt", CultureInfo.InvariantCulture).ToUpperInvariant()} {now:hh:mm:ss}",
                $"{month}. {now:dd} {now:yyyy}"),
            "european" => (
                $"{now:HH:mm:ss}",
                $"{now:dd} {month}. {now:yyyy}"),
            "asian" => (
                $"{now:HH:mm:ss}",
                $"{now:yyyy} {month}. {now:dd}"),
            _ => (
                $"{now:HH:mm:ss}",
                $"{now:yyyy} {month}. {now:dd}"),
        };
    }
}
