using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace GSBT.WinUI;

/// <summary>Parses <c>--minimized</c> / <c>--hidden</c> from the Run-key startup command line.</summary>
internal static class StartupLaunchPresentation
{
    private const int SwHide = 0;
    private const int SwShowMinimized = 2;

    public static bool StartMinimized { get; private set; }
    public static bool StartHidden { get; private set; }

    public static void ParseCommandLine(string[] args)
    {
        foreach (var a in args.Skip(1))
        {
            if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
            {
                StartMinimized = true;
            }

            if (string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase))
            {
                StartHidden = true;
            }
        }

        if (StartHidden)
        {
            StartMinimized = false;
        }
    }

    public static void ApplyIfNeeded(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (StartHidden)
        {
            _ = ShowWindow(hwnd, SwHide);
            return;
        }

        if (StartMinimized)
        {
            _ = ShowWindow(hwnd, SwShowMinimized);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
