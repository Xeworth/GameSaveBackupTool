using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using WinUIEx;
using Windows.Graphics;

namespace GSBT.WinUI.Services;

/// <summary>Sets WinUI <see cref="Window"/> client area via <see cref="AppWindow"/> (XAML Width/Height are unreliable on <see cref="Window"/>).</summary>
public static class WindowSizeHelper
{
    /// <summary>Nominal 800×600 client intent (see <see cref="CalibratedClientSizeForNominal"/>).</summary>
    public const string MainWindowPreset800 = "800x600";

    /// <summary>Nominal 1024×768 client intent; maps to measured <c>1043×780</c> <see cref="AppWindow"/> client (ruler-tuned from 1040×778).</summary>
    public const string MainWindowPreset1024 = "1024x768";

    public const string MainWindowPresetCustom = "custom";

    private const int ReferenceNominalW = 1024;
    private const int ReferenceNominalH = 768;
    private const int ReferenceClientW = 1043;
    private const int ReferenceClientH = 780;

    /// <summary>Minimum client size aligned with <see cref="App"/> WinUIEx limits.</summary>
    public const int MinClientWidth = 656;

    public const int MinClientHeight = 490;

    public static (int W, int H) CalibratedClientSizeForNominal(int nominalW, int nominalH)
    {
        var w = (nominalW * ReferenceClientW + ReferenceNominalW / 2) / ReferenceNominalW;
        var h = (nominalH * ReferenceClientH + ReferenceNominalH / 2) / ReferenceNominalH;
        return (Math.Max(MinClientWidth, w), Math.Max(MinClientHeight, h));
    }

    public static string NormalizeMainWindowPreset(string? preset)
    {
        var p = (preset ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            MainWindowPreset800 => MainWindowPreset800,
            MainWindowPreset1024 => MainWindowPreset1024,
            MainWindowPresetCustom => MainWindowPresetCustom,
            _ => MainWindowPreset800,
        };
    }

    public static bool TryGetClientSize(Window window, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var s = appWindow.Size;
            width = s.Width;
            height = s.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolves a preset tag + optional custom dimensions to client pixels (no settings read).</summary>
    public static (int W, int H) ResolveMainWindowClientSize(string preset, int customW, int customH)
    {
        preset = NormalizeMainWindowPreset(preset);
        return preset switch
        {
            MainWindowPreset1024 => (ReferenceClientW, ReferenceClientH),
            MainWindowPresetCustom when customW >= MinClientWidth && customH >= MinClientHeight => (customW, customH),
            MainWindowPresetCustom => CalibratedClientSizeForNominal(800, 600),
            _ => CalibratedClientSizeForNominal(800, 600),
        };
    }

    /// <summary>Resolves stored preset + optional custom dimensions to client pixels.</summary>
    public static (int W, int H) ResolveMainWindowClientSize(SettingsStore store)
    {
        var preset = NormalizeMainWindowPreset(store.Get("main_window_client_preset", MainWindowPreset800));
        var customW = store.Get("main_window_custom_width", 0);
        var customH = store.Get("main_window_custom_height", 0);
        return ResolveMainWindowClientSize(preset, customW, customH);
    }

    public static void ApplyMainWindowFromSettings(SettingsStore store, Window? window)
    {
        if (window is null)
        {
            return;
        }

        var (w, h) = ResolveMainWindowClientSize(store);
        SetClientSize(window, w, h);
        ApplyMainWindowResizePolicy(store, window);
    }

    /// <summary>Live preview while Settings is open: resize from UI without persisting preset to disk yet.</summary>
    public static void ApplyMainWindowLayoutPreview(Window? window, string presetTag, int customW, int customH, bool lockResolution)
    {
        if (window is null)
        {
            return;
        }

        var (w, h) = ResolveMainWindowClientSize(presetTag, customW, customH);
        SetClientSize(window, w, h);
        ApplyMainWindowResizePolicy(window, lockResolution);
    }

    /// <summary>Maps current client pixels to a stored preset when they match a nominal size (within a few pixels).</summary>
    public static string ClassifyClientPixels(int clientWidth, int clientHeight, int epsilon = 4)
    {
        var (w800, h800) = CalibratedClientSizeForNominal(800, 600);
        if (Math.Abs(clientWidth - w800) <= epsilon && Math.Abs(clientHeight - h800) <= epsilon)
        {
            return MainWindowPreset800;
        }

        if (Math.Abs(clientWidth - ReferenceClientW) <= epsilon && Math.Abs(clientHeight - ReferenceClientH) <= epsilon)
        {
            return MainWindowPreset1024;
        }

        return MainWindowPresetCustom;
    }

    /// <summary>When <c>main_window_lock_resolution</c> is on, the window cannot be resized (current client size is authoritative).</summary>
    public static void ApplyMainWindowResizePolicy(SettingsStore store, Window? window) =>
        ApplyMainWindowResizePolicy(window, store.Get("main_window_lock_resolution", false));

    /// <summary>Applies resizable / locked chrome using an explicit lock flag (e.g. Settings live preview).</summary>
    public static void ApplyMainWindowResizePolicy(Window? window, bool lockResolution)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            presenter.SetBorderAndTitleBar(true, true);
            presenter.IsResizable = !lockResolution;
            presenter.IsMaximizable = !lockResolution;
            presenter.IsMinimizable = true;
        }
        catch
        {
            // ignore
        }
    }

    public static void SetClientSize(Window window, int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));
        }
        catch
        {
            // ignore sizing failures (headless automation, etc.)
        }
    }

    /// <summary>Fixed dialog like PyQt min=max.</summary>
    public static void SetFixedClientSize(Window window, int width, int height)
    {
        SetClientSize(window, width, height);
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter is not null)
            {
                presenter.SetBorderAndTitleBar(true, true);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = true;
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Same minimum client size as the main GSBT window (<see cref="MinClientWidth"/>×<see cref="MinClientHeight"/>).</summary>
    public static void ApplyMinimumClientSize(Window window)
    {
        try
        {
            var wm = WindowManager.Get(window);
            wm.MinWidth = MinClientWidth;
            wm.MinHeight = MinClientHeight;
        }
        catch
        {
            // ignore minimum-size failures on unsupported hosts
        }
    }
}
