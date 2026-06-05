using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GSBT.WinUI.Services;

/// <summary>
/// Colors the classic (non–extended) caption bar to match Fluent dark/light surfaces (#202020 in dark mode).
/// </summary>
public static class TitleBarThemeHelper
{
    public static void Apply(Window window, ElementTheme theme)
    {
        var useDark = theme switch
        {
            ElementTheme.Light => false,
            ElementTheme.Dark => true,
            _ => IsSystemPreferringDarkBackground()
        };

        ApplyImpl(window, useDark);
    }

    public static void ApplyApplicationTheme(Window window, ApplicationTheme appTheme) =>
        ApplyImpl(window, appTheme != ApplicationTheme.Light);

    /// <summary>True when the OS app background color is dark (same heuristic as Fluent caption chrome).</summary>
    public static bool IsSystemPreferringDarkBackground() => IsSystemDarkTheme();

    private static bool IsSystemDarkTheme()
    {
        try
        {
            var c = new UISettings().GetColorValue(UIColorType.Background);
            return c.R < 128;
        }
        catch
        {
            return true;
        }
    }

    private static void ApplyImpl(Window window, bool dark)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            TryApplyDwmImmersiveDarkMode(hwnd, dark);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var titleBar = appWindow.TitleBar;

            if (dark)
            {
                var bg = Color.FromArgb(255, 32, 32, 32);
                var fg = Color.FromArgb(255, 243, 243, 243);
                var inactiveFg = Color.FromArgb(255, 172, 172, 172);
                var hover = Color.FromArgb(255, 58, 58, 58);
                var pressed = Color.FromArgb(255, 72, 72, 72);
                titleBar.BackgroundColor = bg;
                titleBar.ForegroundColor = fg;
                titleBar.InactiveBackgroundColor = bg;
                titleBar.InactiveForegroundColor = inactiveFg;
                titleBar.ButtonBackgroundColor = bg;
                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonInactiveBackgroundColor = bg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = pressed;
                titleBar.ButtonPressedForegroundColor = fg;
            }
            else
            {
                var bg = Color.FromArgb(255, 243, 243, 243);
                var fg = Color.FromArgb(255, 26, 26, 26);
                var inactiveFg = Color.FromArgb(255, 120, 120, 120);
                var hover = Color.FromArgb(255, 229, 229, 229);
                var pressed = Color.FromArgb(255, 204, 204, 204);
                titleBar.BackgroundColor = bg;
                titleBar.ForegroundColor = fg;
                titleBar.InactiveBackgroundColor = bg;
                titleBar.InactiveForegroundColor = inactiveFg;
                titleBar.ButtonBackgroundColor = bg;
                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonInactiveBackgroundColor = bg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = pressed;
                titleBar.ButtonPressedForegroundColor = fg;
            }
        }
        catch
        {
            // ignore (automation, very old OS builds, etc.)
        }
    }

    /// <summary>Win32 caption fallback so minimize/restore does not flash the default white title bar.</summary>
    private static void TryApplyDwmImmersiveDarkMode(IntPtr hwnd, bool dark)
    {
        try
        {
            var useDark = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }
        catch
        {
            // ignore
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("Dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
