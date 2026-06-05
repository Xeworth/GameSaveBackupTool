namespace GSBT.WinUI.Services;

/// <summary>Monitor performance chart series visibility (sandbox settings).</summary>
public static class PerformanceChartDisplaySettings
{
    public const string ShowGsbtKey = "sandbox_perf_show_gsbt";
    public const string ShowCompressionKey = "sandbox_perf_show_compression";

    public const string ShowCheckpointsCombinedKey = "sandbox_perf_show_checkpoints_combined";
    public const string ShowCheckpointsCpuKey = "sandbox_perf_show_checkpoints_cpu";
    public const string ShowCheckpointsRamKey = "sandbox_perf_show_checkpoints_ram";

    /// <summary>Legacy single key; migrated read-only fallback.</summary>
    public const string ShowCheckpointsKey = "sandbox_perf_show_checkpoints";

    public static bool ShowGsbt(SettingsStore store) => store.Get(ShowGsbtKey, true);

    public static bool ShowCompression(SettingsStore store) => store.Get(ShowCompressionKey, true);

    public static bool ShowCheckpoints(PerformanceChartCheckpointScope scope, SettingsStore store)
    {
        var key = scope switch
        {
            PerformanceChartCheckpointScope.Combined => ShowCheckpointsCombinedKey,
            PerformanceChartCheckpointScope.Cpu => ShowCheckpointsCpuKey,
            PerformanceChartCheckpointScope.Ram => ShowCheckpointsRamKey,
            _ => ShowCheckpointsCombinedKey,
        };

        if (store.ContainsKey(key))
        {
            return store.Get(key, true);
        }

        return store.Get(ShowCheckpointsKey, true);
    }
}

public enum PerformanceChartCheckpointScope
{
    Combined,
    Cpu,
    Ram,
}
