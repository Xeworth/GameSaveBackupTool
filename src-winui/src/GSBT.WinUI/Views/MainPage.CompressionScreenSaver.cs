using GSBT.Core.Services;
using GSBT.WinUI;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace GSBT.WinUI.Views;

public partial class MainPage
{
    private CompressionScreenSaverController? _screenSaverController;
    private readonly ScreenSaverWindowResizeSession _screenSaverWindowResize = new();
    private bool _screenSaverSequenceRunning;
    private bool _screenSaverResizePersistSuppressed;
    private bool _screenSaverHadLockedResolution;
    private bool _compressionTrackSubscribed;

    internal void PreviewCompressionScreenSaver()
    {
        EnsureScreenSaverController();
        _screenSaverController!.ForcePreview();
    }

    internal void SimulateSlowCompressForScreenSaver(int durationSeconds = 25)
    {
        EnsureScreenSaverController();
        _screenSaverController!.StartSlowCompressSimulation(durationSeconds);
    }

    private void EnsureScreenSaverController()
    {
        if (_screenSaverController is null)
        {
            _screenSaverController = new CompressionScreenSaverController(ViewModel, _settingsStore, DispatcherQueue);
            _screenSaverController.EnterRequested += ScreenSaverController_EnterRequested;
            _screenSaverController.ExitRequested += ScreenSaverController_ExitRequested;
            CompressionScreenSaver.ExitRequested += CompressionScreenSaver_ExitRequested;
            CompressionScreenSaver.TrackEnded += CompressionScreenSaver_TrackEnded;
            CompressionScreenSaver.Configure(_settingsStore.Get("date_format", GSBT.Core.Common.BackupDateFormatter.DefaultFormatKey));
        }

        if (!_compressionTrackSubscribed)
        {
            ViewModel.CompressionActivity.TrackChanged += () => SyncCompressionFileTracker();
            _compressionTrackSubscribed = true;
        }
    }

    private bool IsPerFileArchiveMode()
    {
        if (ViewModel.FooterCompressShowsCancel)
        {
            return !ViewModel.GetCompressionOptionsForBackupRun().SolidArchive;
        }

        return !_settingsStore.Get(CompressionOptionsResolver.SolidArchiveSettingsKey, true);
    }

    private void ConfigureScreenSaverFileTracker()
    {
        var enabled = IsPerFileArchiveMode()
            && (ViewModel.FooterCompressShowsCancel || _screenSaverController?.IsActive == true);
        CompressionScreenSaver.SetFileTrackerEnabled(enabled);
        if (enabled)
        {
            ApplyCompressionFileTrackerToOverlay();
        }
        else
        {
            CompressionScreenSaver.ClearFileTracker();
        }
    }

    private void ApplyCompressionFileTrackerToOverlay()
    {
        if (!IsPerFileArchiveMode())
        {
            CompressionScreenSaver.ClearFileTracker();
            return;
        }

        var tracker = ViewModel.CompressionActivity;
        CompressionScreenSaver.UpdateFileTracker(
            tracker.UpcomingGameFolder,
            tracker.CurrentGameFolder,
            tracker.PreviousGameFolder);
    }

    private void SyncCompressionFileTracker()
    {
        if (_screenSaverController is not { IsActive: true })
        {
            return;
        }

        ConfigureScreenSaverFileTracker();
        CompressionScreenSaver.EnsureFileTrackerChromeVisible();
    }

    private async void ScreenSaverController_EnterRequested()
    {
        if (_screenSaverSequenceRunning)
        {
            return;
        }

        _screenSaverSequenceRunning = true;
        SetScreenSaverResizePersistSuppressed(true);
        try
        {
            var window = App.MainWindowRef;
            var isMinimized = window is not null && WindowSizeHelper.IsWindowIconic(window);
            var asset = ResolveScreenSaverAsset();

            CompressionScreenSaver.Configure(_settingsStore.Get("date_format", GSBT.Core.Common.BackupDateFormatter.DefaultFormatKey), asset.Id);
            CompressionScreenSaver.AssetRotationEnabled = IsScreenSaverRotationEnabled();
            ConfigureScreenSaverFileTracker();
            ApplyCompressionFileTrackerToOverlay();
            ScreenSaverProgressTheme.ApplyTheme(
                ProgressTrack,
                ProgressStripGrid,
                asset.ProgressThemeKey,
                animateIn: true);

            if (window is not null)
            {
                if (!isMinimized
                    && WindowSizeHelper.TryGetClientSize(window, out var clientW, out var clientH))
                {
                    _screenSaverWindowResize.NoteVisibleLayoutMetrics(MainContentArea, clientW, clientH);
                }

                _screenSaverWindowResize.CaptureRestoreSize(window, _settingsStore);
                _screenSaverHadLockedResolution = _settingsStore.Get("main_window_lock_resolution", false);
                if (_screenSaverHadLockedResolution)
                {
                    WindowSizeHelper.ApplyMainWindowResizePolicy(window, false);
                }
            }

            if (isMinimized)
            {
                GamesTable.Opacity = 0;
                await CompressionScreenSaver.ShowAsync(skipAnimations: true);
                CompressionScreenSaver.EnsureFileTrackerChromeVisible();
                if (window is not null)
                {
                    await _screenSaverWindowResize.AnimateToFitVideoAsync(
                        window,
                        MainContentArea,
                        asset.VideoWidth,
                        asset.VideoHeight,
                        DispatcherQueue);
                }
            }
            else
            {
                var resizeTask = window is not null
                    ? _screenSaverWindowResize.AnimateToFitVideoAsync(
                        window,
                        MainContentArea,
                        asset.VideoWidth,
                        asset.VideoHeight,
                        DispatcherQueue)
                    : Task.CompletedTask;
                var fadeTask = AnimateDoubleOnTargetAsync(GamesTable, 1, 0, 320);
                await Task.WhenAll(resizeTask, fadeTask);
                await CompressionScreenSaver.ShowAsync();
                CompressionScreenSaver.EnsureFileTrackerChromeVisible();
            }
        }
        finally
        {
            _screenSaverSequenceRunning = false;
        }
    }

    private async void ScreenSaverController_ExitRequested()
    {
        while (_screenSaverSequenceRunning)
        {
            await Task.Delay(50);
        }

        _screenSaverSequenceRunning = true;
        SetScreenSaverResizePersistSuppressed(true);
        try
        {
            await CompressionScreenSaver.HideAsync();

            if (App.MainWindowRef is { } window)
            {
                if (_screenSaverHadLockedResolution)
                {
                    WindowSizeHelper.ApplyMainWindowResizePolicy(window, true);
                }

                await _screenSaverWindowResize.RestoreAsync(window, DispatcherQueue);
                _screenSaverWindowResize.SyncSettingsAfterRestore(_settingsStore);
                WindowSizeHelper.ApplyMainWindowResizePolicy(_settingsStore, window);
                _screenSaverHadLockedResolution = false;
            }

            await AnimateDoubleOnTargetAsync(GamesTable, GamesTable.Opacity, 1, 360);
            ScreenSaverProgressTheme.RestoreDefault(ProgressTrack, ProgressStripGrid, animateOut: true);
        }
        finally
        {
            _screenSaverSequenceRunning = false;
            CancelMainWindowResizePersistTimer();
            SetScreenSaverResizePersistSuppressed(false);
        }
    }

    private void SetScreenSaverResizePersistSuppressed(bool suppressed)
    {
        _screenSaverResizePersistSuppressed = suppressed;
        if (suppressed)
        {
            CancelMainWindowResizePersistTimer();
        }
    }

    private bool ShouldSuppressMainWindowResizePersist =>
        _suppressMainWindowResizePersist
        || _screenSaverResizePersistSuppressed
        || ViewModel.FooterBackupShowsCancel
        || ViewModel.FooterCompressShowsCancel;

    private void CompressionScreenSaver_ExitRequested(object? sender, EventArgs e) =>
        _screenSaverController?.NotifyUserExit();

    private async void CompressionScreenSaver_TrackEnded(object? sender, EventArgs e)
    {
        if (_screenSaverController is not { IsActive: true })
        {
            return;
        }

        if (!IsScreenSaverRotationEnabled())
        {
            return;
        }

        while (_screenSaverSequenceRunning)
        {
            await Task.Delay(50);
        }

        _screenSaverSequenceRunning = true;
        try
        {
            var next = ScreenSaverRotationStore.PickNext();
            await CompressionScreenSaver.TransitionToAssetAsync(next.Id);
            await ScreenSaverProgressTheme.TransitionThemeAsync(
                ProgressTrack,
                ProgressStripGrid,
                next.ProgressThemeKey);
        }
        finally
        {
            _screenSaverSequenceRunning = false;
        }
    }

    private bool IsScreenSaverRotationEnabled()
    {
        if (ScreenSaverAssetCatalog.AvailableSets().Count <= 1)
        {
            return false;
        }

        if (_screenSaverController?.IsPreviewMode == true)
        {
            var forcedId = TryGetSandboxForcedScreenSaverAssetId();
            return forcedId is null or 0;
        }

        return true;
    }

    internal void SyncCompressionScreenSaverCancelStatus() =>
        CompressionScreenSaver.SetCancelRequested(ViewModel.OperationCancelRequested);

    internal void SyncCompressionScreenSaverFileTracker() => SyncCompressionFileTracker();

    private ScreenSaverAssetSet ResolveScreenSaverAsset()
    {
        int? forcedId = null;
        if (_screenSaverController?.IsPreviewMode == true)
        {
            forcedId = TryGetSandboxForcedScreenSaverAssetId();
        }

        return ScreenSaverRotationStore.PickNext(forcedId);
    }

    private static int? TryGetSandboxForcedScreenSaverAssetId()
    {
        try
        {
            if (App.Host?.Services.GetService<SandboxSimulationState>() is not { } state)
            {
                return null;
            }

            return state.ScreenSaverPreviewAssetId > 0 ? state.ScreenSaverPreviewAssetId : null;
        }
        catch
        {
            return null;
        }
    }

    private static Task AnimateDoubleOnTargetAsync(DependencyObject target, double from, double to, int durationMs)
    {
        var tcs = new TaskCompletionSource<bool>();
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        anim.Completed += (_, _) => tcs.TrySetResult(true);
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
        _ = Task.Delay(durationMs + 80).ContinueWith(_ => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private void InitializeCompressionScreenSaver()
    {
        EnsureScreenSaverController();
        Canvas.SetZIndex(CompressionScreenSaver, 2);
    }

    private void DisposeCompressionScreenSaver()
    {
        if (_screenSaverController is null)
        {
            return;
        }

        _screenSaverController.EnterRequested -= ScreenSaverController_EnterRequested;
        _screenSaverController.ExitRequested -= ScreenSaverController_ExitRequested;
        CompressionScreenSaver.ExitRequested -= CompressionScreenSaver_ExitRequested;
        CompressionScreenSaver.TrackEnded -= CompressionScreenSaver_TrackEnded;
        _screenSaverController.Dispose();
        _screenSaverController = null;
    }
}
