namespace GSBT.WinUI;

/// <summary>Unified %AppData%\Roaming\GSBT paths (logs live under a subfolder).</summary>
internal static class AppPaths
{
    public static string RoamingGsbtRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSBT");

    public static string LogsDirectory => Path.Combine(RoamingGsbtRoot, "logs");

    public static string WinUiCrashLogPath => Path.Combine(LogsDirectory, "winui_last_error.txt");

    /// <summary>Sandbox monitor → Benchmark tab: persisted compression run records (JSON).</summary>
    public static string SandboxCompressionBenchmarksPath =>
        Path.Combine(RoamingGsbtRoot, "sandbox_compression_benchmarks.json");

    /// <summary>Migrate crash log from legacy %LocalAppData%\GSBT if present.</summary>
    public static void MigrateLegacyCrashLogIfNeeded()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GSBT",
                "winui_last_error.txt");
            if (File.Exists(legacy) && !File.Exists(WinUiCrashLogPath))
            {
                File.Copy(legacy, WinUiCrashLogPath, overwrite: false);
            }
        }
        catch
        {
            // ignore
        }
    }
}
