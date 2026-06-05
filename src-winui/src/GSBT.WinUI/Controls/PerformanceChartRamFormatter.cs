namespace GSBT.WinUI.Controls;

/// <summary>Formats RAM sample values (MiB internally) for sandbox performance UI.</summary>
public static class PerformanceChartRamFormatter
{
    private const double GibThresholdMiB = 1024.0;

    public static string FormatRamMiB(double memMiB, double systemMemMb = 0, bool includePercentOfRam = false)
    {
        var size = FormatSize(memMiB);
        if (!includePercentOfRam || systemMemMb <= 0.001)
        {
            return size;
        }

        var pct = memMiB / systemMemMb * 100.0;
        return $"{size} ({pct:0.#}% RAM)";
    }

    public static string FormatSize(double memMiB)
    {
        if (memMiB >= GibThresholdMiB)
        {
            return $"{memMiB / GibThresholdMiB:0.##} GiB";
        }

        return $"{memMiB:0.#} MiB";
    }

    public static string FormatHoverLine(string label, double memMiB, double systemMemMb, bool asPercentOfRam)
    {
        if (asPercentOfRam && systemMemMb > 0.001)
        {
            return $"{label}: {FormatRamMiB(memMiB, systemMemMb, includePercentOfRam: true)}";
        }

        return $"{label}: {FormatSize(memMiB)}";
    }
}
