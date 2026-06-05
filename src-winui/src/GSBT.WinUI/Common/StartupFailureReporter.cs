using System.Runtime.InteropServices;

namespace GSBT.WinUI.Common;

/// <summary>User-visible errors and crash logs when startup fails (installer / Program Files launches).</summary>
internal static class StartupFailureReporter
{
    private const uint MbIconError = 0x00000010;

    public static void InstallGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Report(ex, "AppDomain.Unhandled");
                ShowErrorDialog(ex, "AppDomain.Unhandled");
            }
        };
    }

    public static void ReportAndExit(Exception ex, string stage)
    {
        Report(ex, stage);
        ShowErrorDialog(ex, stage);
        Environment.Exit(1);
    }

    public static void Report(Exception ex, string stage)
    {
        var body =
            $"{DateTime.UtcNow:O} UTC [{stage}]\n" +
            $"Exe: {Environment.ProcessPath}\n" +
            $"BaseDirectory: {AppContext.BaseDirectory}\n" +
            $"CurrentDirectory: {Environment.CurrentDirectory}\n\n" +
            ex;

        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "gsbt_winui_last_error.txt");
            File.WriteAllText(temp, body);
        }
        catch
        {
            // ignore
        }

        try
        {
            AppPaths.MigrateLegacyCrashLogIfNeeded();
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            File.WriteAllText(AppPaths.WinUiCrashLogPath, body);
        }
        catch
        {
            // ignore
        }
    }

    public static void ShowErrorDialog(Exception ex, string stage)
    {
        var logHint = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GSBT",
            "logs",
            "winui_last_error.txt");
        var text =
            "Game Save Backup Tool could not start.\n\n" +
            $"Stage: {stage}\n" +
            $"{ex.GetType().Name}: {ex.Message}\n\n" +
            "Full details were written to:\n" +
            logHint + "\n\n" +
            Path.Combine(Path.GetTempPath(), "gsbt_winui_last_error.txt");

        _ = MessageBox(IntPtr.Zero, text, AppAboutInfo.AppName, MbIconError);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
