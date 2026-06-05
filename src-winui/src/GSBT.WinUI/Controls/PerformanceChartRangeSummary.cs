namespace GSBT.WinUI.Controls;

/// <summary>Aggregated metrics for a drag-selected time range on performance charts.</summary>
public sealed class PerformanceChartRangeSummary
{
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public double DurationSeconds { get; init; }
    public int SampleCount { get; init; }

    public double GsbtCpuMax { get; init; }
    public double GsbtCpuAvg { get; init; }
    public double CompressCpuMax { get; init; }
    public double CompressCpuAvg { get; init; }

    public double GsbtMemMax { get; init; }
    public double GsbtMemAvg { get; init; }
    public double CompressMemMax { get; init; }
    public double CompressMemAvg { get; init; }

    public IReadOnlyList<string> GamesZipped { get; init; } = Array.Empty<string>();

    public static PerformanceChartRangeSummary Compute(
        int startIdx,
        int endIdx,
        double sampleIntervalSeconds,
        double[]? gsbtCpu,
        double[]? compressCpu,
        double[]? gsbtMem = null,
        double[]? compressMem = null,
        string[]? activityLabels = null,
        IReadOnlyList<(long Serial, string Game)>? compressionGameEvents = null,
        long historyStartSerial = 0)
    {
        var lo = Math.Min(startIdx, endIdx);
        var hi = Math.Max(startIdx, endIdx);
        var count = hi - lo + 1;
        return new PerformanceChartRangeSummary
        {
            StartIndex = lo,
            EndIndex = hi,
            SampleCount = count,
            DurationSeconds = Math.Max(0, count - 1) * sampleIntervalSeconds,
            GsbtCpuMax = MaxInRange(gsbtCpu, lo, hi),
            GsbtCpuAvg = AvgInRange(gsbtCpu, lo, hi),
            CompressCpuMax = MaxInRange(compressCpu, lo, hi),
            CompressCpuAvg = AvgInRange(compressCpu, lo, hi),
            GsbtMemMax = MaxInRange(gsbtMem, lo, hi),
            GsbtMemAvg = AvgInRange(gsbtMem, lo, hi),
            CompressMemMax = MaxInRange(compressMem, lo, hi),
            CompressMemAvg = AvgInRange(compressMem, lo, hi),
            GamesZipped = DistinctGamesInRange(activityLabels, lo, hi, compressionGameEvents, historyStartSerial),
        };
    }

    public static IReadOnlyList<string> DistinctGamesInRange(
        string[]? activityLabels,
        int lo,
        int hi,
        IReadOnlyList<(long Serial, string Game)>? compressionGameEvents = null,
        long historyStartSerial = 0)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        if (activityLabels is { Length: > 0 })
        {
            for (var i = lo; i <= hi && i < activityLabels.Length; i++)
            {
                if (i < 0)
                {
                    continue;
                }

                var game = activityLabels[i]?.Trim();
                if (string.IsNullOrEmpty(game) || !seen.Add(game))
                {
                    continue;
                }

                list.Add(game);
            }
        }

        if (compressionGameEvents is { Count: > 0 })
        {
            var serialLo = historyStartSerial + lo;
            var serialHi = historyStartSerial + hi;
            foreach (var (serial, game) in compressionGameEvents)
            {
                if (serial < serialLo || serial > serialHi)
                {
                    continue;
                }

                var trimmed = game.Trim();
                if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }

                list.Add(trimmed);
            }
        }

        if (list.Count == 0)
        {
            return Array.Empty<string>();
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static double MaxInRange(double[]? series, int lo, int hi)
    {
        if (series is null || series.Length == 0)
        {
            return 0;
        }

        var max = 0.0;
        for (var i = lo; i <= hi && i < series.Length; i++)
        {
            if (i >= 0 && series[i] > max)
            {
                max = series[i];
            }
        }

        return max;
    }

    private static double AvgInRange(double[]? series, int lo, int hi)
    {
        if (series is null || series.Length == 0)
        {
            return 0;
        }

        var sum = 0.0;
        var n = 0;
        for (var i = lo; i <= hi && i < series.Length; i++)
        {
            if (i < 0)
            {
                continue;
            }

            sum += series[i];
            n++;
        }

        return n == 0 ? 0 : sum / n;
    }
}
