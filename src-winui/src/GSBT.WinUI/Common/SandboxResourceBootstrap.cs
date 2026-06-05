using System.Runtime.InteropServices;

namespace GSBT.WinUI.Common;

/// <summary>
/// WinUI unpackaged MRT loads <c>{ProcessName}.pri</c> beside the exe.
/// <c>gsbt-sandbox.exe</c> is a hard link to <c>gsbt.exe</c>, so it also needs <c>gsbt-sandbox.pri</c>.
/// </summary>
internal static class SandboxResourceBootstrap
{
    private const string MainExeName = "gsbt.exe";
    private const string SandboxExeName = "gsbt-sandbox.exe";
    private const string MainPriName = "gsbt.pri";
    private const string SandboxPriName = "gsbt-sandbox.pri";

    public static void EnsureSandboxPriAlias()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return;
        }

        if (!string.Equals(Path.GetFileName(processPath), SandboxExeName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        var sandboxPri = Path.Combine(baseDir, SandboxPriName);
        if (File.Exists(sandboxPri))
        {
            return;
        }

        var mainPri = Path.Combine(baseDir, MainPriName);
        if (!File.Exists(mainPri))
        {
            return;
        }

        try
        {
            if (!NativeMethods.CreateHardLink(sandboxPri, mainPri, IntPtr.Zero))
            {
                _ = Marshal.GetLastWin32Error();
            }
        }
        catch
        {
            // Installer should create the link; dev can run launch_sandbox.bat (-s) instead.
        }
    }

    private static class NativeMethods
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
