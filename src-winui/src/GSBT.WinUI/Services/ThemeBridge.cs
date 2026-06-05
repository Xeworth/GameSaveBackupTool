using GSBT.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Services;

/// <summary>
/// Normalizes persisted <c>ui_theme</c> keys, resolves <c>system</c> to explicit Light/Dark, and applies
/// <see cref="Application.RequestedTheme"/> plus the open <see cref="MainPage"/> so <c>ThemeResource</c> tokens track.
/// </summary>
public static class ThemeBridge
{
    /// <summary>Fired when the main shell or sandbox monitor applies an explicit light/dark theme.</summary>
    public static event Action<ElementTheme>? ShellThemeChanged;

    public static string NormalizeUiThemeKey(string? uiThemeKey)
    {
        var t = (uiThemeKey ?? string.Empty).Trim().ToLowerInvariant();
        if (t is "" or "default")
        {
            return "dark";
        }

        return t is "light" or "dark" or "system" ? t : "dark";
    }

    public static ElementTheme ResolveMainPageElementTheme(string? uiThemeKey) =>
        NormalizeUiThemeKey(uiThemeKey) switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            "system" => TitleBarThemeHelper.IsSystemPreferringDarkBackground() ? ElementTheme.Dark : ElementTheme.Light,
            _ => ElementTheme.Dark
        };

    public static ApplicationTheme ResolveApplicationTheme(string? uiThemeKey) =>
        ResolveMainPageElementTheme(uiThemeKey) == ElementTheme.Light
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;

    /// <summary>Maps persisted settings keys to WinUI element theme (system resolves to explicit Light/Dark).</summary>
    public static ElementTheme ResolveElementTheme(string? uiThemeKey) => ResolveMainPageElementTheme(uiThemeKey);

    /// <summary>Sets <see cref="Application.RequestedTheme"/> and applies the same resolved theme to the open main page and title bar.</summary>
    public static void ApplyFromUiThemeKey(string? uiThemeKey)
    {
        var key = NormalizeUiThemeKey(uiThemeKey);
        try
        {
            if (Application.Current is not null)
            {
                Application.Current.RequestedTheme = ResolveApplicationTheme(key);
            }
        }
        catch
        {
            // ignore
        }

        ApplyElementThemeToOpenMainPage(key);
    }

    public static void ApplyElementThemeToOpenMainPage(string? uiThemeKey)
    {
        var key = NormalizeUiThemeKey(uiThemeKey);
        if (App.MainWindowRef?.Content is not Frame f || f.Content is not MainPage mp)
        {
            return;
        }

        var resolved = ResolveMainPageElementTheme(key);
        mp.RequestedTheme = resolved;
        TitleBarThemeHelper.Apply(App.MainWindowRef, resolved);
        mp.SyncSandboxMonitorChromeTheme(resolved);
        RaiseShellThemeChanged(resolved);

        _ = mp.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            if (App.MainWindowRef is not null)
            {
                TitleBarThemeHelper.Apply(App.MainWindowRef, resolved);
            }

            mp.SyncSandboxMonitorChromeTheme(resolved);
            RaiseShellThemeChanged(resolved);
        });

        _ = mp.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            mp.SyncSandboxMonitorChromeTheme(resolved);
            RaiseShellThemeChanged(resolved);
        });
    }

    private static void RaiseShellThemeChanged(ElementTheme theme)
    {
        try
        {
            ShellThemeChanged?.Invoke(theme);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>True when explicit shell theme is dark (prefers <see cref="MainPage.RequestedTheme"/> over lagging ActualTheme).</summary>
    public static bool IsDarkChrome(FrameworkElement? element = null)
    {
        if (element?.RequestedTheme is ElementTheme.Light)
        {
            return false;
        }

        if (element?.RequestedTheme is ElementTheme.Dark)
        {
            return true;
        }

        return IsShellDarkTheme();
    }

    public static ElementTheme ResolveChromeTheme(FrameworkElement? element = null) =>
        IsDarkChrome(element) ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>
    /// Prefer <see cref="FrameworkElement.RequestedTheme"/> on <see cref="MainPage"/> when it is explicit Light/Dark —
    /// <see cref="FrameworkElement.ActualTheme"/> can still reflect the previous theme for a frame during live toggles,
    /// which breaks code paths that snapshot colors from the shell theme.
    /// </summary>
    public static bool IsShellDarkTheme()
    {
        try
        {
            if (App.MainWindowRef?.Content is Frame f && f.Content is MainPage mp)
            {
                return mp.RequestedTheme switch
                {
                    ElementTheme.Light => false,
                    ElementTheme.Dark => true,
                    _ => mp.ActualTheme == ElementTheme.Dark
                };
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            return Application.Current.RequestedTheme != ApplicationTheme.Light;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Palette-aligned brushes for code paths where <see cref="Application.Current.Resources"/>.TryGetValue returns
    /// the wrong <c>ThemeDictionaries</c> branch.
    /// </summary>
    public static Brush GetGsbtBrush(bool useDarkChrome, string key) =>
        (key, useDarkChrome) switch
        {
            ("GsbtWindowBgBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x20, 0x20, 0x20)),
            ("GsbtWindowBgBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xf3, 0xf3, 0xf3)),
            ("GsbtCardBgBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x2d, 0x2d, 0x2d)),
            ("GsbtCardBgBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xfb, 0xfb, 0xfb)),
            ("GsbtBorderBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x3d, 0x3d, 0x3d)),
            ("GsbtBorderBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xe0, 0xe0, 0xe0)),
            ("GsbtBodyTextBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xf3, 0xf3, 0xf3)),
            ("GsbtBodyTextBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x1a, 0x1a, 0x1a)),
            ("GsbtSecondaryLabelBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x9d, 0x9d, 0x9d)),
            ("GsbtSecondaryLabelBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x76, 0x76, 0x76)),
            ("GsbtTableGridLineBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x3e, 0x3e, 0x42)),
            ("GsbtTableGridLineBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xd0, 0xd0, 0xd0)),
            ("GsbtMutedTextBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xc8, 0xc8, 0xc8)),
            ("GsbtMutedTextBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x5c, 0x5c, 0x5c)),
            ("GsbtEstimateStatBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x78, 0xbe, 0xff)),
            ("GsbtEstimateStatBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x0b, 0x5c, 0xad)),
            ("GsbtEstimateGoodBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x81, 0xf0, 0xae)),
            ("GsbtEstimateGoodBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x1b, 0x6b, 0x3f)),
            ("GsbtEstimateWarnBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xff, 0xd5, 0x4f)),
            ("GsbtEstimateWarnBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xb2, 0x53, 0x00)),
            ("GsbtEstimateBadBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xef, 0x53, 0x50)),
            ("GsbtEstimateBadBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xc6, 0x28, 0x28)),
            ("GsbtBenchmarkSuccessTitleBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0x6a, 0xc2, 0x5b)),
            ("GsbtBenchmarkSuccessTitleBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x1b, 0x6b, 0x3f)),
            ("GsbtBenchmarkFailureTitleBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xd4, 0x6b, 0x6b)),
            ("GsbtBenchmarkFailureTitleBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0xb7, 0x1c, 0x1c)),
            ("GsbtMonoBodyBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xd8, 0xd8, 0xd8)),
            ("GsbtMonoBodyBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x1a, 0x1a, 0x1a)),
            ("GsbtMonoMutedBrush", true) => new SolidColorBrush(Color.FromArgb(255, 0xa8, 0xa8, 0xa8)),
            ("GsbtMonoMutedBrush", false) => new SolidColorBrush(Color.FromArgb(255, 0x5c, 0x5c, 0x5c)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0x80, 0x80, 0x80)),
        };
}
