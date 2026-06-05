using System.Globalization;

namespace GSBT.Core.Common;

/// <summary>Formats catalog <c>last_backup</c> ISO timestamps for the grid (Python <c>format_backup_date</c> parity).</summary>
public static class BackupDateFormatter
{
    /// <param name="isoTimestamp">UTC ISO string from catalog, or empty.</param>
    /// <param name="formatKey"><c>iso</c>, <c>us</c>, <c>european</c>, or <c>asian</c>.</param>
    public static string FormatDisplay(string? isoTimestamp, string? formatKey)
    {
        var key = (formatKey ?? "iso").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(isoTimestamp))
        {
            return "Not yet backed up";
        }

        if (!DateTime.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return isoTimestamp;
        }

        return key switch
        {
            "us" => dt.ToString("MM/dd/yyyy | hh:mm tt", CultureInfo.InvariantCulture),
            "european" => dt.ToString("dd/MM/yyyy | HH:mm", CultureInfo.InvariantCulture),
            "asian" => dt.ToString("yyyy/MM/dd | HH:mm", CultureInfo.InvariantCulture),
            _ => dt.ToString("yyyy-MM-dd | HH:mm", CultureInfo.InvariantCulture)
        };
    }
}
