namespace GSBT.WinUI.Common;

/// <summary>Central names for the public GSBT entry points and branding assets.</summary>
public static class AppIdentity
{
    public const string CliExecutableName = "gsbt.exe";
    public const string GuiExecutableName = "gsbt-main.exe";
    public const string SandboxExecutableName = "gsbt-sandbox.exe";

    public const string GuiPriName = "gsbt-main.pri";
    public const string SandboxPriName = "gsbt-sandbox.pri";

    public const string MainIconFileName = "gsbt.ico";
    public const string SandboxIconFileName = "gsbt-s.ico";

    public const string SandboxDisplayName = "GSBT Sandbox";

    public static bool IsSandboxExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileName(executablePath),
            SandboxExecutableName,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSandboxSession(bool launchSandboxMonitor, bool isSandboxSimulationChild) =>
        launchSandboxMonitor && !isSandboxSimulationChild;

    public static string IconFileNameForSession(bool sandboxSession) =>
        sandboxSession ? SandboxIconFileName : MainIconFileName;
}
