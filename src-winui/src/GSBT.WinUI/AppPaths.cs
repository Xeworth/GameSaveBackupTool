using GSBT.Core.Common;

namespace GSBT.WinUI;

/// <summary>WinUI paths under <c>%AppData%\Roaming\GSBT\winui</c>.</summary>
internal static class AppPaths
{
    public static string WinUiUserDataRoot => UserDataDir.GetWinUiUserDataDir();

    public static string LogsDirectory => Path.Combine(WinUiUserDataRoot, "logs");

    public static string WinUiCrashLogPath => Path.Combine(LogsDirectory, "winui_last_error.txt");

    /// <summary>Sandbox monitor → Benchmark tab: persisted compression run records (JSON).</summary>
    public static string SandboxCompressionBenchmarksPath =>
        Path.Combine(WinUiUserDataRoot, "sandbox_compression_benchmarks.json");

    /// <summary>Migrate crash log from legacy flat GSBT / LocalAppData locations if present.</summary>
    public static void MigrateLegacyCrashLogIfNeeded()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var legacyPaths = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    UserDataDir.GsbtAppName,
                    "winui_last_error.txt"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    UserDataDir.GsbtAppName,
                    "logs",
                    "winui_last_error.txt"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    UserDataDir.GsbtAppName,
                    "winui_last_error.txt"),
            };

            foreach (var legacy in legacyPaths)
            {
                if (File.Exists(legacy) && !File.Exists(WinUiCrashLogPath))
                {
                    File.Copy(legacy, WinUiCrashLogPath, overwrite: false);
                    break;
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
