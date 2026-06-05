using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace GSBT.WinUI.Services;

/// <summary>Resolves <c>branding/*.ico</c> next to the built exe and applies them via <see cref="AppWindow.SetIcon"/>.</summary>
public static class AppBrandingIcons
{
    public const string MainIconFileName = "gsbt.ico";
    public const string SandboxIconFileName = "gsbt-s.ico";

    /// <summary>Main shell icon for normal launch; sandbox session uses <see cref="SandboxIconFileName"/>.</summary>
    public static string IconFileNameForSession(bool sandboxSession) =>
        sandboxSession ? SandboxIconFileName : MainIconFileName;

    public static bool TryResolveIconPath(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        foreach (var candidate in EnumerateIconPathCandidates(fileName))
        {
            if (File.Exists(candidate))
            {
                fullPath = Path.GetFullPath(candidate);
                return true;
            }
        }

        return false;
    }

    public static void TryApplyToWindow(Window? window, string fileName)
    {
        if (window is null || !TryResolveIconPath(fileName, out var path))
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).SetIcon(path);
        }
        catch
        {
            // Optional chrome — ignore if SetIcon is unavailable.
        }
    }

    public static void TryApplySessionIcon(Window? window, bool sandboxSession) =>
        TryApplyToWindow(window, IconFileNameForSession(sandboxSession));

    private static IEnumerable<string> EnumerateIconPathCandidates(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "branding", fileName);

        var dir = baseDir;
        for (var depth = 0; depth < 10 && !string.IsNullOrWhiteSpace(dir); depth++)
        {
            yield return Path.Combine(dir, "branding", fileName);
            yield return Path.Combine(dir, "src-winui", "branding", fileName);
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
    }
}
