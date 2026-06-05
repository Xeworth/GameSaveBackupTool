using System.Runtime.InteropServices;
using GSBT.WinUI;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.UI;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxMonitorWindow : Window
{
    private const int SwRestore = 9;
    private const int SwMinimize = 6;

    private readonly SandboxLogHub _hub;
    private readonly SandboxSimulationState _simulation;
    private readonly SandboxResourceMonitor _resourceMonitor;

    private SandboxStatesView? _statesView;
    private SandboxBenchmarkView? _benchmarkView;
    private SandboxBatchBenchmarkView? _batchView;
    private SandboxLogView? _logView;
    private SandboxPerformanceView? _performanceView;
    private SandboxMonitorSettingsView? _settingsView;

    private bool _closeMainWhenMonitorClosed;
    private bool _syncCloseBypassPrompt;
    private bool _syncCloseDialogInFlight;
    private bool _batchSyncCloseDialogInFlight;
    private bool _batchSyncCloseConfirmedForThisClose;
    private SandboxBatchPerformanceHub? _batchHub;
    private bool _performanceBadgeDismissed;

    /// <summary>When true, closing this monitor does not cascade-close the main window (e.g. main app is exiting first).</summary>
    public bool SuppressSyncCloseMain { get; set; }

    public SandboxMonitorWindow(SandboxLogHub hub, SandboxSimulationState simulation, SandboxResourceMonitor resourceMonitor)
    {
        _hub = hub;
        _simulation = simulation;
        _resourceMonitor = resourceMonitor;
        InitializeComponent();

        Title = "GSBT Sandbox Monitor";
        ExtendsContentIntoTitleBar = false;
        WindowSizeHelper.ApplyMinimumClientSize(this);
        AppBrandingIcons.TryApplyToWindow(this, AppBrandingIcons.SandboxIconFileName);

        AppWindow.Closing += AppWindow_OnClosing;
        Closed += SandboxMonitorWindow_Closed;
        Activated += SandboxMonitorWindow_Activated;

        RootNav.SelectedItem = SimulationsNavItem;
        WireFooterLabelsToNavigationPane();
        WirePerformanceNavBadge();
        PullInitialThemeFromMainOrSettings();
        _resourceMonitor.Start();
        _ = ShowPageAsync("simulations");
    }

    private void WirePerformanceNavBadge()
    {
        try
        {
            _batchHub = App.Host?.Services.GetRequiredService<SandboxBatchPerformanceHub>();
            if (_batchHub is null)
            {
                return;
            }

            _batchHub.StateChanged += BatchHub_OnStateChanged;
            UpdatePerformanceNavBadge();
        }
        catch
        {
            // Host/services unavailable in unusual launch paths.
        }
    }

    private void BatchHub_OnStateChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_batchHub?.IsActive == true)
            {
                _performanceBadgeDismissed = false;
            }

            UpdatePerformanceNavBadge();
        });
    }

    private void UpdatePerformanceNavBadge()
    {
        if (PerformanceNavItem is null)
        {
            return;
        }

        if (_performanceBadgeDismissed || _batchHub is null)
        {
            PerformanceNavItem.InfoBadge = null;
            return;
        }

        var tests = _batchHub.Tests;
        if (_batchHub.IsActive || tests.Any(t => t.Phase == BatchTestRunPhase.Running))
        {
            PerformanceNavItem.InfoBadge = CreatePerformanceNavInfoBadge(0xE6, 0xB4, 0x22);
            return;
        }

        if (tests.Count == 0)
        {
            PerformanceNavItem.InfoBadge = null;
            return;
        }

        if (tests.Any(t => t.Phase is BatchTestRunPhase.Failed or BatchTestRunPhase.Cancelled))
        {
            PerformanceNavItem.InfoBadge = CreatePerformanceNavInfoBadge(0xE8, 0x11, 0x23);
        }
        else if (tests.All(t => t.Phase == BatchTestRunPhase.Completed))
        {
            PerformanceNavItem.InfoBadge = CreatePerformanceNavInfoBadge(0x4C, 0xAF, 0x50);
        }
        else
        {
            PerformanceNavItem.InfoBadge = null;
        }
    }

    private static InfoBadge CreatePerformanceNavInfoBadge(byte r, byte g, byte b) =>
        new()
        {
            Background = new SolidColorBrush(Color.FromArgb(255, r, g, b)),
        };

    private void PullInitialThemeFromMainOrSettings()
    {
        try
        {
            if (App.MainWindowRef?.Content is Frame f && f.Content is MainPage mp)
            {
                var target = mp.RequestedTheme is ElementTheme.Light or ElementTheme.Dark
                    ? mp.RequestedTheme
                    : (mp.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
                ApplyShellChromeTheme(target);
                return;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var store = App.Host!.Services.GetRequiredService<SettingsStore>();
            ApplyShellChromeTheme(ThemeBridge.ResolveElementTheme(store.Get("ui_theme", "dark")));
        }
        catch
        {
            ApplyShellChromeTheme(ElementTheme.Dark);
        }
    }

    private void WireFooterLabelsToNavigationPane()
    {
        RootNav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => UpdateFooterLabelsForPaneOpen());
        UpdateFooterLabelsForPaneOpen();
    }

    private void UpdateFooterLabelsForPaneOpen()
    {
        var show = RootNav.IsPaneOpen;
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        ShowMainFooterLabel.Visibility = vis;
        MinimizeBothFooterLabel.Visibility = vis;
        ShowMainAppBarButton.HorizontalContentAlignment = show ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        MinimizeBothAppBarButton.HorizontalContentAlignment = show ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
    }

    private void SandboxMonitorWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        TryResyncChromeFromMainPage();
    }

    /// <summary>Catch-up when the main shell theme and this window drift (e.g. missed sync during a transition).</summary>
    private void TryResyncChromeFromMainPage()
    {
        try
        {
            if (App.MainWindowRef?.Content is not Frame f || f.Content is not MainPage mp)
            {
                return;
            }

            var target = mp.RequestedTheme is ElementTheme.Light or ElementTheme.Dark
                ? mp.RequestedTheme
                : (mp.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
            if (RootNav.RequestedTheme != target)
            {
                ApplyShellChromeTheme(target);
                return;
            }

            // RequestedTheme can already match while caption / NavigationView template chrome is stale (secondary window).
            TitleBarThemeHelper.Apply(this, target);
            InvalidateShellChromeLayout();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Raised after <see cref="ApplyShellChromeTheme"/> updates monitor chrome (detail popups subscribe for live theme).</summary>
    public event Action<ElementTheme>? ShellChromeThemeChanged;

    /// <summary>Keeps the monitor in sync with the main shell (RequestedTheme + caption bar) when the user changes app theme.</summary>
    public void ApplyShellChromeTheme(ElementTheme theme)
    {
        var t = theme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        try
        {
            RootNav.RequestedTheme = t;
            ShellHostGrid.RequestedTheme = t;
            ShellContent.RequestedTheme = t;
            if (_statesView is not null)
            {
                _statesView.RequestedTheme = t;
            }

            if (_benchmarkView is not null)
            {
                _benchmarkView.RequestedTheme = t;
                _benchmarkView.OnShellThemeChanged(t);
            }

            if (_batchView is not null)
            {
                _batchView.RequestedTheme = t;
            }

            if (_logView is not null)
            {
                _logView.RequestedTheme = t;
            }

            if (_performanceView is not null)
            {
                _performanceView.RequestedTheme = t;
                _performanceView.OnShellThemeChanged(t);
            }

            if (_settingsView is not null)
            {
                _settingsView.RequestedTheme = t;
            }

            TitleBarThemeHelper.Apply(this, t);
            InvalidateShellChromeLayout();
            ShellChromeThemeChanged?.Invoke(t);

            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    TitleBarThemeHelper.Apply(this, t);
                    InvalidateShellChromeLayout();
                }
                catch
                {
                    // ignore
                }
            });
        }
        catch
        {
            // ignore
        }
    }

    private void InvalidateShellChromeLayout()
    {
        try
        {
            RootNav.InvalidateMeasure();
            RootNav.InvalidateArrange();
            ShellHostGrid.InvalidateMeasure();
            ShellHostGrid.InvalidateArrange();
        }
        catch
        {
            // ignore
        }
    }

    private void SandboxMonitorWindow_Closed(object sender, WindowEventArgs args)
    {
        Activated -= SandboxMonitorWindow_Activated;
        _batchView?.RequestCancelBatch();
        _resourceMonitor.Stop();
        _batchSyncCloseConfirmedForThisClose = false;
        if (_closeMainWhenMonitorClosed && !SuppressSyncCloseMain)
        {
            try
            {
                App.MainWindowRef?.Close();
            }
            catch
            {
            }
        }
    }

    private void AppWindow_OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (SuppressSyncCloseMain)
        {
            return;
        }

        try
        {
            var store = App.Host?.Services.GetRequiredService<SettingsStore>();
            if (store is null || !store.Get("sandbox_monitor_sync_close", false))
            {
                return;
            }

            if (_batchSyncCloseConfirmedForThisClose)
            {
                _closeMainWhenMonitorClosed = true;
                return;
            }

            var session = App.Host!.Services.GetRequiredService<SandboxMonitorSession>();
            if (session.IsBatchBenchmarkRunning)
            {
                args.Cancel = true;
                if (!_batchSyncCloseDialogInFlight)
                {
                    _ = ShowBatchSyncCloseDialogAsync(store);
                }

                return;
            }

            if (store.Get("sandbox_monitor_sync_close_skip_confirm", false) || _syncCloseBypassPrompt)
            {
                _closeMainWhenMonitorClosed = true;
                return;
            }

            if (_syncCloseDialogInFlight)
            {
                args.Cancel = true;
                return;
            }

            args.Cancel = true;
            _ = ShowSyncCloseDialogAsync(store);
        }
        catch
        {
            // allow close if settings/host unavailable
        }
    }

    private async Task ShowBatchSyncCloseDialogAsync(SettingsStore store)
    {
        var xr = Content?.XamlRoot;
        if (xr is null)
        {
            return;
        }

        _batchSyncCloseDialogInFlight = true;
        try
        {
            var dlg = new ContentDialog
            {
                Title = "Batch benchmark running",
                XamlRoot = xr,
                PrimaryButtonText = "Close both anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Text =
                        "A batch compression is still running in this monitor. Closing the Sandbox Monitor will stop that work. "
                        + "Because sync-close is enabled, the main Game Save Backup Tool window will close too.\n\n"
                        + "This warning always appears while a batch is running (same idea as closing the main window during a batch). "
                        + "Esc cancels; Tab moves focus between buttons.",
                },
            };

            var r = await GsbtContentDialog.ShowAsync(dlg);
            if (r != ContentDialogResult.Primary)
            {
                return;
            }

            _batchSyncCloseConfirmedForThisClose = true;
            _closeMainWhenMonitorClosed = true;
            Close();
        }
        finally
        {
            _batchSyncCloseDialogInFlight = false;
        }
    }

    private async Task ShowSyncCloseDialogAsync(SettingsStore store)
    {
        var xr = Content?.XamlRoot;
        if (xr is null)
        {
            return;
        }

        _syncCloseDialogInFlight = true;
        try
        {
            var dontAsk = new CheckBox { Content = "Don't ask again", IsChecked = false };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(
                new TextBlock
                {
                    Text = "Closing the Sandbox Monitor will also close the main Game Save Backup Tool window. Change this under Monitor → Settings. Tab to the checkboxes; Space toggles; Esc closes this dialog as Cancel.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            panel.Children.Add(dontAsk);

            var dlg = new ContentDialog
            {
                Title = "Close main app too?",
                Content = panel,
                PrimaryButtonText = "Close both",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xr,
            };

            var r = await GsbtContentDialog.ShowAsync(dlg);
            if (r != ContentDialogResult.Primary)
            {
                return;
            }

            if (dontAsk.IsChecked == true)
            {
                store.Set("sandbox_monitor_sync_close_skip_confirm", true);
            }

            _syncCloseBypassPrompt = true;
            _closeMainWhenMonitorClosed = true;
            Close();
        }
        finally
        {
            _syncCloseDialogInFlight = false;
        }
    }

    private async void RootNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            await ShowPageAsync(tag);
        }
    }

    private async Task ShowPageAsync(string tag)
    {
        switch (tag)
        {
            case "simulations":
                ShellContent.Content = _statesView ??= new SandboxStatesView(_simulation);
                break;
            case "benchmark":
                var bv = _benchmarkView ??= new SandboxBenchmarkView(
                    App.Host!.Services.GetRequiredService<MainViewModel>(),
                    App.Host!.Services.GetRequiredService<SandboxCompressionBenchmarkStore>(),
                    App.Host!.Services.GetRequiredService<SandboxLogHub>(),
                    App.Host!.Services.GetRequiredService<SandboxMonitorSession>(),
                    this);
                ShellContent.Content = bv;
                await bv.EnsureHistoryLoadedAsync().ConfigureAwait(true);
                break;
            case "batch":
                ShellContent.Content = _batchView ??= new SandboxBatchBenchmarkView(
                    App.Host!.Services.GetRequiredService<MainViewModel>(),
                    App.Host!.Services.GetRequiredService<SandboxCompressionBenchmarkStore>(),
                    App.Host!.Services.GetRequiredService<SandboxLogHub>(),
                    App.Host!.Services.GetRequiredService<SettingsStore>(),
                    App.Host!.Services.GetRequiredService<SandboxMonitorSession>(),
                    App.Host!.Services.GetRequiredService<SandboxBatchPerformanceHub>(),
                    App.Host!.Services.GetRequiredService<SandboxResourceMonitor>(),
                    App.Host!.Services.GetRequiredService<CompressionActivityTracker>(),
                    OnBatchRecordedAsync);
                break;
            case "performance":
                _performanceBadgeDismissed = true;
                UpdatePerformanceNavBadge();
                ShellContent.Content = _performanceView ??= new SandboxPerformanceView(
                    App.Host!.Services.GetRequiredService<SandboxResourceMonitor>(),
                    App.Host!.Services.GetRequiredService<SandboxBatchPerformanceHub>(),
                    App.Host!.Services.GetRequiredService<SettingsStore>(),
                    this);
                break;
            case "log":
                ShellContent.Content = _logView ??= new SandboxLogView(_hub);
                break;
            case "settings":
                ShellContent.Content = _settingsView ??= new SandboxMonitorSettingsView(
                    App.Host!.Services.GetRequiredService<SettingsStore>(),
                    App.Host!.Services.GetRequiredService<SandboxMonitorSession>(),
                    App.Host!.Services.GetRequiredService<SandboxLogHub>());
                break;
        }

        if (ShellContent.Content is FrameworkElement pageRoot)
        {
            pageRoot.RequestedTheme = RootNav.RequestedTheme;
        }
    }

    private async Task OnBatchRecordedAsync()
    {
        if (_benchmarkView is not null)
        {
            await _benchmarkView.ReloadHistoryAsync().ConfigureAwait(true);
        }
    }

    private void ShowMainAppBarButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateMainToolboxWindow();
    }

    private void MinimizeBothAppBarButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindowRef is Window mw)
            {
                _ = ShowWindow(WindowNative.GetWindowHandle(mw), SwMinimize);
            }
        }
        catch
        {
        }

        try
        {
            _ = ShowWindow(WindowNative.GetWindowHandle(this), SwMinimize);
        }
        catch
        {
        }
    }

    private static void ActivateMainToolboxWindow()
    {
        if (App.MainWindowRef is not Window mw)
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(mw);
            _ = ShowWindow(hwnd, SwRestore);
            _ = SetForegroundWindow(hwnd);
        }
        catch
        {
        }

        try
        {
            mw.Activate();
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
