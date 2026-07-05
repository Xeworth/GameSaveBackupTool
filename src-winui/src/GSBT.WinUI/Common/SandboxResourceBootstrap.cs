using System.Runtime.InteropServices;

namespace GSBT.WinUI.Common;

/// <summary>
/// WinUI unpackaged MRT loads <c>{ProcessName}.pri</c> beside the exe.
/// <c>gsbt-sandbox.exe</c> is a copy of <c>gsbt-main.exe</c>, so it also needs <c>gsbt-sandbox.pri</c>.
/// </summary>
internal static class SandboxResourceBootstrap
{
    public static void EnsureSandboxPriAlias()
    {
        var processPath = Environment.ProcessPath;
        if (!AppIdentity.IsSandboxExecutablePath(processPath))
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        var sandboxPri = Path.Combine(baseDir, AppIdentity.SandboxPriName);
        if (File.Exists(sandboxPri))
        {
            return;
        }

        var mainPri = Path.Combine(baseDir, AppIdentity.GuiPriName);
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
            // Release packaging should create the alias; dev launches can use -s without the sandbox exe.
        }
    }

    private static class NativeMethods
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
