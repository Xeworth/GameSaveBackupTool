using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace GSBT.WinUI.Services;

/// <summary>Win32 show/hide for “minimize to tray” (WinUI <see cref="Window"/> stays alive).</summary>
internal static class MainWindowTrayVisibility
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    [DllImport("USER32", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void Hide(Window window)
    {
        ApplyTitleBarBeforeTrayTransition(window);
        var hwnd = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(hwnd, SwHide);
    }

    public static void ShowAndActivate(Window window)
    {
        ApplyTitleBarBeforeTrayTransition(window);
        var hwnd = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(hwnd, SwRestore);
        try
        {
            window.Activate();
        }
        catch
        {
            _ = ShowWindow(hwnd, SwShow);
        }

        ApplyTitleBarBeforeTrayTransition(window);
    }

    private static void ApplyTitleBarBeforeTrayTransition(Window window)
    {
        try
        {
            if (window.Content is FrameworkElement root)
            {
                var theme = root.RequestedTheme is ElementTheme.Light or ElementTheme.Dark
                    ? root.RequestedTheme
                    : root.ActualTheme;
                TitleBarThemeHelper.Apply(window, theme);
                return;
            }

            TitleBarThemeHelper.ApplyApplicationTheme(window, Application.Current.RequestedTheme);
        }
        catch
        {
            // ignore
        }
    }
}
