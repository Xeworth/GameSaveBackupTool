using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace GSBT.WinUI.Services;

/// <summary>Layout captured before a screen-saver resize animation.</summary>
internal readonly record struct ScreenSaverRestoreLayout(
    string Preset,
    int CustomWidth,
    int CustomHeight,
    int ClientWidth,
    int ClientHeight);

/// <summary>Smoothly resizes the main window so a video aspect ratio fits the content host.</summary>
internal sealed class ScreenSaverWindowResizeSession
{
    private SizeInt32 _savedSize;
    private ScreenSaverRestoreLayout _restoreLayout;
    private bool _hasSaved;
    private double _lastKnownContentW;
    private double _lastKnownContentH;
    private int _lastKnownClientW;
    private int _lastKnownClientH;
    private int _chromeBelowContent;

    public ScreenSaverRestoreLayout RestoreLayout => _restoreLayout;

    /// <summary>Cache content metrics while the window is laid out in the foreground.</summary>
    public void NoteVisibleLayoutMetrics(FrameworkElement contentHost, int clientWidth, int clientHeight)
    {
        if (clientWidth > 0 && clientHeight > 0)
        {
            _lastKnownClientW = clientWidth;
            _lastKnownClientH = clientHeight;
        }

        if (contentHost.ActualWidth <= 1 || contentHost.ActualHeight <= 1)
        {
            return;
        }

        _lastKnownContentW = contentHost.ActualWidth;
        _lastKnownContentH = contentHost.ActualHeight;
        var chrome = clientHeight - contentHost.ActualHeight;
        if (chrome >= 40)
        {
            _chromeBelowContent = (int)Math.Round(chrome);
        }
    }

    /// <summary>Record preset and client size before any screen-saver resize animation.</summary>
    public void CaptureRestoreSize(Window window, SettingsStore settings)
    {
        var (clientW, clientH) = ResolvePreScreenSaverClientSize(window, settings);
        var preset = WindowSizeHelper.ClassifyClientPixels(clientW, clientH);
        var customW = settings.Get("main_window_custom_width", 0);
        var customH = settings.Get("main_window_custom_height", 0);
        if (preset == WindowSizeHelper.MainWindowPresetCustom)
        {
            customW = clientW;
            customH = clientH;
        }

        _savedSize = new SizeInt32(clientW, clientH);
        _restoreLayout = new ScreenSaverRestoreLayout(preset, customW, customH, clientW, clientH);
        _hasSaved = true;
    }

    public async Task AnimateToFitVideoAsync(
        Window window,
        FrameworkElement contentHost,
        int videoWidth,
        int videoHeight,
        DispatcherQueue dispatcher)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || !_hasSaved)
        {
            return;
        }

        NoteVisibleLayoutMetrics(contentHost, _restoreLayout.ClientWidth, _restoreLayout.ClientHeight);
        if (!TryComputeVideoFitTarget(contentHost, videoWidth, videoHeight, out var target))
        {
            return;
        }

        if (Math.Abs(target.Width - _restoreLayout.ClientWidth) < 2
            && Math.Abs(target.Height - _restoreLayout.ClientHeight) < 2)
        {
            return;
        }

        if (WindowSizeHelper.IsWindowIconic(window))
        {
            WindowSizeHelper.SetClientSize(window, target.Width, target.Height);
            return;
        }

        await AnimateClientSizeAsync(
            window,
            _restoreLayout.ClientWidth,
            _restoreLayout.ClientHeight,
            target.Width,
            target.Height,
            dispatcher);
    }

    public async Task RestoreAsync(Window window, DispatcherQueue dispatcher)
    {
        if (!_hasSaved)
        {
            return;
        }

        var targetW = _savedSize.Width;
        var targetH = _savedSize.Height;

        if (WindowSizeHelper.IsWindowIconic(window))
        {
            WindowSizeHelper.SetClientSize(window, targetW, targetH);
            _hasSaved = false;
            return;
        }

        if (!WindowSizeHelper.TryGetClientSize(window, out var clientW, out var clientH))
        {
            WindowSizeHelper.SetClientSize(window, targetW, targetH);
            _hasSaved = false;
            return;
        }

        await AnimateClientSizeAsync(window, clientW, clientH, targetW, targetH, dispatcher);
        _hasSaved = false;
    }

    public void SyncSettingsAfterRestore(SettingsStore settings)
    {
        var layout = _restoreLayout;
        if (layout.ClientWidth <= 0 || layout.ClientHeight <= 0)
        {
            return;
        }

        settings.Set("main_window_client_preset", layout.Preset);
        if (layout.Preset == WindowSizeHelper.MainWindowPresetCustom)
        {
            settings.Set("main_window_custom_width", layout.ClientWidth);
            settings.Set("main_window_custom_height", layout.ClientHeight);
        }
    }

    private (int Width, int Height) ResolvePreScreenSaverClientSize(Window window, SettingsStore settings)
    {
        if (_lastKnownClientW > 0 && _lastKnownClientH > 0)
        {
            return (_lastKnownClientW, _lastKnownClientH);
        }

        if (!WindowSizeHelper.IsWindowIconic(window)
            && WindowSizeHelper.TryGetClientSize(window, out var liveW, out var liveH))
        {
            return (liveW, liveH);
        }

        if (WindowSizeHelper.TryGetNormalClientSize(window, out var normalW, out var normalH))
        {
            return (normalW, normalH);
        }

        return WindowSizeHelper.ResolveMainWindowClientSize(settings);
    }

    private bool TryComputeVideoFitTarget(
        FrameworkElement contentHost,
        int videoWidth,
        int videoHeight,
        out SizeInt32 target)
    {
        target = default;
        var baselineW = _restoreLayout.ClientWidth;
        var baselineH = _restoreLayout.ClientHeight;
        if (baselineW <= 0 || baselineH <= 0)
        {
            return false;
        }

        if (!TryResolveContentAreaSize(contentHost, baselineW, baselineH, out var contentW, out var contentH))
        {
            return false;
        }

        var targetContentH = contentW * videoHeight / (double)videoWidth;
        var deltaH = (int)Math.Round(targetContentH - contentH);
        if (Math.Abs(deltaH) < 6)
        {
            target = new SizeInt32(baselineW, baselineH);
            return true;
        }

        target = new SizeInt32(
            baselineW,
            Math.Max(WindowSizeHelper.MinClientHeight, baselineH + deltaH));
        return true;
    }

    private bool TryResolveContentAreaSize(
        FrameworkElement contentHost,
        int baselineClientW,
        int baselineClientH,
        out double contentW,
        out double contentH)
    {
        contentW = contentHost.ActualWidth;
        contentH = contentHost.ActualHeight;
        if (contentW > 1 && contentH > 1)
        {
            _lastKnownContentW = contentW;
            _lastKnownContentH = contentH;
            return true;
        }

        if (_lastKnownContentW > 1 && _lastKnownContentH > 1)
        {
            contentW = _lastKnownContentW;
            contentH = _lastKnownContentH;
            return true;
        }

        if (_chromeBelowContent > 0)
        {
            contentW = Math.Max(1, baselineClientW);
            contentH = Math.Max(1, baselineClientH - _chromeBelowContent);
            return contentW > 1 && contentH > 1;
        }

        return false;
    }

    private static async Task AnimateClientSizeAsync(
        Window window,
        int fromW,
        int fromH,
        int toW,
        int toH,
        DispatcherQueue dispatcher)
    {
        const int steps = 18;
        const int stepMs = 22;
        for (var step = 1; step <= steps; step++)
        {
            var t = step / (double)steps;
            t = 1 - Math.Pow(1 - t, 3);
            var w = (int)Math.Round(fromW + (toW - fromW) * t);
            var h = (int)Math.Round(fromH + (toH - fromH) * t);
            var tcs = new TaskCompletionSource<bool>();
            dispatcher.TryEnqueue(() =>
            {
                WindowSizeHelper.SetClientSize(window, w, h);
                tcs.TrySetResult(true);
            });
            await tcs.Task;
            if (step < steps)
            {
                await Task.Delay(stepMs);
            }
        }
    }
}
