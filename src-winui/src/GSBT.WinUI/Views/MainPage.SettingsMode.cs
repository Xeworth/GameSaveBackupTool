using System.Threading;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace GSBT.WinUI.Views;

/// <summary>In-page settings: footer tab strip + OK/Cancel, cross-fade with the main command bar.</summary>
public partial class MainPage
{
    private SettingsPanel? _settingsPanel;
    private bool _settingsOpen;
    private bool _compressionEngineTeachingTipProgrammaticClose;
    private readonly SemaphoreSlim _settingsTransitionLock = new(1, 1);
    private int _settingsTransitionGeneration;

    private async Task EnterSettingsAsync()
    {
        await _settingsTransitionLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_settingsOpen)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _settingsTransitionGeneration);

            // Rebuild each open so generated controls always pick up the current theme dictionary.
            _settingsPanel = new SettingsPanel(
                ViewModel,
                _settingsStore,
                () => GamesTable.RefreshThemeVisuals(),
                ApplyUiThemeWithShellSoftTransitionAsync);
            SettingsPanelContainer.Child = _settingsPanel;
            _settingsPanel.ReloadFields();
            _settingsPanel.SelectTab(0);
            SyncSettingsFooterTabs(0);

            _settingsOpen = true;
            SyncBackupIntegrityStripUi();

            SetSettingsTransitionStartVisuals(entering: true);
            await AnimateSettingsTransitionAsync(toSettings: true).ConfigureAwait(true);

            if (generation != Volatile.Read(ref _settingsTransitionGeneration) || !_settingsOpen)
            {
                SnapSettingsUiToCurrentState();
                return;
            }

            SetSettingsTransitionEndVisuals(entering: true);
            SyncSettingsFooterTabs(_settingsPanel.SelectedTab);
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (_settingsPanel is not null && _settingsOpen)
                    {
                        SyncSettingsFooterTabs(_settingsPanel.SelectedTab);
                    }
                });
        }
        finally
        {
            _settingsTransitionLock.Release();
        }
    }

    private async Task ExitSettingsAsync()
    {
        await _settingsTransitionLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!_settingsOpen)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _settingsTransitionGeneration);
            _settingsOpen = false;

            CloseCompressionEngineTeachingTipProgrammatically();
            try
            {
                _settingsPanel?.CloseSevenZipGetVsWebsiteTeachingTipProgrammatically();
            }
            catch
            {
                // ignore
            }

            try
            {
                SettingsAfterCompressTeachingTip.IsOpen = false;
            }
            catch
            {
                // ignore
            }

            SetSettingsTransitionStartVisuals(entering: false);
            await AnimateSettingsTransitionAsync(toSettings: false).ConfigureAwait(true);

            if (generation != Volatile.Read(ref _settingsTransitionGeneration))
            {
                SnapSettingsUiToCurrentState();
                return;
            }

            SetSettingsTransitionEndVisuals(entering: false);
            FinalizeSettingsClosed();
        }
        finally
        {
            _settingsTransitionLock.Release();
        }
    }

    private void SetSettingsTransitionStartVisuals(bool entering)
    {
        MainContentArea.Visibility = Visibility.Visible;
        NormalFooterBorder.Visibility = Visibility.Visible;
        ProgressStripGrid.Visibility = Visibility.Visible;
        SettingsPanelContainer.Visibility = Visibility.Visible;
        SettingsFooterBorder.Visibility = Visibility.Visible;

        if (entering)
        {
            MainContentArea.Opacity = 1;
            NormalFooterBorder.Opacity = 1;
            SettingsPanelContainer.Opacity = 0;
            SettingsFooterBorder.Opacity = 0;
        }
        else
        {
            MainContentArea.Opacity = 0;
            NormalFooterBorder.Opacity = 0;
            SettingsPanelContainer.Opacity = 1;
            SettingsFooterBorder.Opacity = 1;
        }
    }

    private void SetSettingsTransitionEndVisuals(bool entering)
    {
        if (entering)
        {
            MainContentArea.Visibility = Visibility.Collapsed;
            NormalFooterBorder.Visibility = Visibility.Collapsed;
            ProgressStripGrid.Visibility = Visibility.Collapsed;
            MainContentArea.Opacity = 0;
            NormalFooterBorder.Opacity = 0;
            SettingsPanelContainer.Visibility = Visibility.Visible;
            SettingsFooterBorder.Visibility = Visibility.Visible;
            SettingsPanelContainer.Opacity = 1;
            SettingsFooterBorder.Opacity = 1;
            return;
        }

        SettingsPanelContainer.Visibility = Visibility.Collapsed;
        SettingsFooterBorder.Visibility = Visibility.Collapsed;
        SettingsPanelContainer.Opacity = 0;
        SettingsFooterBorder.Opacity = 0;
        MainContentArea.Visibility = Visibility.Visible;
        NormalFooterBorder.Visibility = Visibility.Visible;
        ProgressStripGrid.Visibility = Visibility.Visible;
        MainContentArea.Opacity = 1;
        NormalFooterBorder.Opacity = 1;
    }

    /// <summary>When a settings animation is superseded, snap to the authoritative open/closed state.</summary>
    private void SnapSettingsUiToCurrentState()
    {
        if (_settingsOpen)
        {
            SetSettingsTransitionEndVisuals(entering: true);
            return;
        }

        SetSettingsTransitionEndVisuals(entering: false);
        FinalizeSettingsClosed();
    }

    private void FinalizeSettingsClosed()
    {
        SettingsPanelContainer.Child = null;
        _settingsPanel = null;

        ThemeBridge.ApplyFromUiThemeKey(_settingsStore.Get("ui_theme", "dark"));
        GamesTable.RefreshThemeVisuals();
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => GamesTable.RefreshThemeVisuals());
        SyncBackupIntegrityStripUi();
        ApplyMainWindowSizeFromSettingsWithSuppress();
    }

    private async Task AnimateSettingsTransitionAsync(bool toSettings)
    {
        var sb = new Storyboard();

        void AddOpacity(UIElement target, double from, double to)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(260),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, "Opacity");
            sb.Children.Add(anim);
        }

        if (toSettings)
        {
            AddOpacity(MainContentArea, 1, 0);
            AddOpacity(SettingsPanelContainer, 0, 1);
            AddOpacity(NormalFooterBorder, 1, 0);
            AddOpacity(SettingsFooterBorder, 0, 1);
        }
        else
        {
            AddOpacity(MainContentArea, 0, 1);
            AddOpacity(SettingsPanelContainer, 1, 0);
            AddOpacity(NormalFooterBorder, 0, 1);
            AddOpacity(SettingsFooterBorder, 1, 0);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? s, object e)
        {
            sb.Completed -= OnCompleted;
            tcs.TrySetResult();
        }

        sb.Completed += OnCompleted;
        sb.Begin();
        var done = await Task.WhenAny(tcs.Task, Task.Delay(450));
        if (!ReferenceEquals(done, tcs.Task))
        {
            sb.Completed -= OnCompleted;
            sb.Stop();
        }
    }

    /// <summary>Marks the active settings section with <c>AccentButtonStyle</c>; others use <c>DefaultButtonStyle</c> (classic Fluent buttons).</summary>
    /// <remarks>Keyboard: <c>Ctrl+Tab</c> / <c>Ctrl+Shift+Tab</c> cycle tabs; <c>Esc</c> closes settings without saving.</remarks>
    private void SyncSettingsFooterTabs(int selectedIndex)
    {
        selectedIndex = Math.Clamp(selectedIndex, 0, 2);
        var accent = LookupApplicationButtonStyle("AccentButtonStyle");
        var normal = LookupApplicationButtonStyle("DefaultButtonStyle");

        SettingsTabBackup.Style = selectedIndex == 0 ? accent : normal;
        SettingsTabCompress.Style = selectedIndex == 1 ? accent : normal;
        SettingsTabSystem.Style = selectedIndex == 2 ? accent : normal;
    }

    private static Style? LookupApplicationButtonStyle(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var o) ? o as Style : null;
    }

    private async void SettingsFooterTab_Click(object sender, RoutedEventArgs e)
    {
        var panel = _settingsPanel;
        if (!_settingsOpen || panel is null)
        {
            return;
        }

        if (sender is not Button btn || !TryParseFooterTabTag(btn.Tag, out var idx))
        {
            return;
        }

        if (idx == panel.SelectedTab)
        {
            return;
        }

        var previousTab = panel.SelectedTab;
        if (previousTab == 1 && idx != 1)
        {
            CloseCompressionEngineTeachingTipProgrammatically();
            panel.CloseSevenZipGetVsWebsiteTeachingTipProgrammatically();
        }

        // Match keyboard path: update footer immediately so accent follows the click, not the 200ms panel cross-fade.
        SyncSettingsFooterTabs(idx);
        await panel.SelectTabAnimatedAsync(idx);
        SyncSettingsFooterTabs(panel.SelectedTab);
        await MaybeShowCompressionEngineTeachingTipAsync();
    }

    private static bool TryParseFooterTabTag(object? tag, out int index)
    {
        index = -1;
        if (tag is int i && i >= 0 && i <= 2)
        {
            index = i;
            return true;
        }

        return int.TryParse(tag?.ToString(), out index) && index >= 0 && index <= 2;
    }

    private async void SettingsOk_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsPanel is null)
        {
            return;
        }

        var saved = await _settingsPanel.SaveAsync();
        if (saved)
        {
            GamesTable.RefreshColumnLayout();
            _ = ViewModel.RefreshBackupSizeDisplaysAsync();
            if (_settingsStore.Get("in_app_ephemeral_status_enabled", true))
            {
                _ = ShowStatusToastAsync("Settings saved.");
            }
        }

        await ExitSettingsAsync();
    }

    private async void SettingsCancel_Click(object sender, RoutedEventArgs e)
    {
        _settingsPanel?.ReloadFields();
        await ExitSettingsAsync();
    }

    private async void SettingsTabNext_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var panel = _settingsPanel;
        if (!_settingsOpen || panel is null)
        {
            return;
        }

        var cur = panel.SelectedTab;
        var next = (cur + 1) % 3;
        if (cur == 1 && next != 1)
        {
            CloseCompressionEngineTeachingTipProgrammatically();
            panel.CloseSevenZipGetVsWebsiteTeachingTipProgrammatically();
        }

        SyncSettingsFooterTabs(next);
        await panel.SelectTabAnimatedAsync(next);
        await MaybeShowCompressionEngineTeachingTipAsync();
        args.Handled = true;
    }

    private async void SettingsTabPrev_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var panel = _settingsPanel;
        if (!_settingsOpen || panel is null)
        {
            return;
        }

        var cur = panel.SelectedTab;
        var prev = (cur + 2) % 3;
        if (cur == 1 && prev != 1)
        {
            CloseCompressionEngineTeachingTipProgrammatically();
            panel.CloseSevenZipGetVsWebsiteTeachingTipProgrammatically();
        }

        SyncSettingsFooterTabs(prev);
        await panel.SelectTabAnimatedAsync(prev);
        await MaybeShowCompressionEngineTeachingTipAsync();
        args.Handled = true;
    }

    private async void SettingsEscape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CanCancelOperation)
        {
            ViewModel.CancelOperation();
            args.Handled = true;
            return;
        }

        if (IsAnyEphemeralToastVisible())
        {
            _ = ClearAllEphemeralToastsForEscapeAsync();
            args.Handled = true;
            return;
        }

        if (StatusToastBorder.Visibility == Visibility.Visible && _statusToastCts is not null)
        {
            CancelStatusToastTokensOnly();
            args.Handled = true;
            return;
        }

        if (!_settingsOpen)
        {
            return;
        }

        args.Handled = true;
        _settingsPanel?.ReloadFields();
        await ExitSettingsAsync();
    }

    private void CompressionEngineTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        var programmatic =
            _compressionEngineTeachingTipProgrammaticClose
            || args.Reason == TeachingTipCloseReason.Programmatic;
        _compressionEngineTeachingTipProgrammaticClose = false;
        if (!programmatic)
        {
            ViewModel.MarkCompressionSevenZipEngineTipDismissed();
        }
    }

    private void CloseCompressionEngineTeachingTipProgrammatically()
    {
        try
        {
            if (!CompressionEngineTeachingTip.IsOpen)
            {
                return;
            }

            _compressionEngineTeachingTipProgrammaticClose = true;
            CompressionEngineTeachingTip.IsOpen = false;
        }
        catch
        {
            _compressionEngineTeachingTipProgrammaticClose = false;
        }
    }

    private async Task MaybeShowCompressionEngineTeachingTipAsync()
    {
        if (!_settingsOpen || _settingsPanel is null || _settingsPanel.SelectedTab != 1)
        {
            return;
        }

        await Task.Delay(480);
        if (!_settingsOpen || _settingsPanel is null || _settingsPanel.SelectedTab != 1)
        {
            return;
        }

        if (ViewModel.ShouldShowCompressionSevenZipEngineTip() && !CompressionEngineTeachingTip.IsOpen)
        {
            try
            {
                CompressionEngineTeachingTip.Subtitle = ViewModel.CompressionSevenZipEngineTeachingTipIntro;
                CompressionEngineTeachingTip.Target = SettingsTabCompress;
                CompressionEngineTeachingTip.IsOpen = true;
            }
            catch
            {
                // ignore tip failures
            }
        }
    }
}
