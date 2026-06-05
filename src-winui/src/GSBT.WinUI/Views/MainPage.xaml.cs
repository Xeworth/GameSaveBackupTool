using System.ComponentModel;

using System.Diagnostics;

using System.Threading;

using System.Runtime.InteropServices;

using GSBT.WinUI;
using GSBT.WinUI.Services;

using GSBT.Core.Services;
using GSBT.WinUI.Common;
using GSBT.WinUI.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml;

using Microsoft.UI.Windowing;

using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Controls.Primitives;

using Microsoft.UI.Xaml.Input;

using Microsoft.UI.Xaml.Media;

using Microsoft.UI.Xaml.Media.Animation;

using Microsoft.UI.Xaml.Documents;

using Microsoft.UI.Dispatching;

using WinRT.Interop;

using Windows.UI;

using Windows.UI.ViewManagement;

using Microsoft.UI.Text;

namespace GSBT.WinUI.Views;



public partial class MainPage : Page

{

    public MainViewModel ViewModel { get; }

    private readonly WinUiTrayService _trayService;

    private SandboxMonitorWindow? _sandboxWindow;
    private CancellationTokenSource? _progressHideCts;

    private SettingsStore _settingsStore = null!;

    private DispatcherTimer? _mainWindowResizePersistTimer;

    private bool _suppressMainWindowResizePersist;

    private bool _forceMainWindowClose;

    private AppWindow? _mainAppWindow;

    private readonly SemaphoreSlim _contentDialogMutex = new(1, 1);

    /// <summary>Skips mirroring ListView selection into <see cref="MainViewModel"/> during programmatic rebuilds.</summary>
    private bool _suppressGamesSelectionSync;

    /// <summary>Tracks animated visibility so open/close transitions stay consistent with settings overlay.</summary>
    private bool _integrityStripUiShown;

    private Windows.UI.ViewManagement.UISettings? _uiSettingsForSystemTheme;

    private ListView GamesGrid => GamesTable.RowsListView;



    public MainPage()

    {

        ViewModel = App.Host?.Services.GetRequiredService<MainViewModel>() ?? throw new InvalidOperationException("Host unavailable.");

        _trayService = App.Host.Services.GetRequiredService<WinUiTrayService>();

        InitializeComponent();

        RegisterPropertyChangedCallback(
            RequestedThemeProperty,
            (_, _) =>
            {
                ScheduleGameTableThemeRefresh();
                SyncSandboxMonitorChromeTheme();
            });

        ActualThemeChanged += MainPage_ActualThemeChanged;

        DataContext = ViewModel;

        _settingsStore = App.Host!.Services.GetRequiredService<SettingsStore>();
        WireViewModelStatusToasts();

        Loaded += MainPage_Loaded;

        Unloaded += MainPage_Unloaded;

    }

    private void MainPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyMainWindowTitleBarTheme();
        SyncSandboxMonitorChromeTheme();
    }

    private void ScheduleGameTableThemeRefresh()
    {
        try
        {
            GamesTable?.RefreshThemeVisuals();
        }
        catch
        {
            // ignore
        }
    }

    private void HookSystemThemeColorListener()

    {

        try

        {

            _uiSettingsForSystemTheme ??= new UISettings();

            _uiSettingsForSystemTheme.ColorValuesChanged -= UiSettingsForSystemTheme_ColorValuesChanged;

            _uiSettingsForSystemTheme.ColorValuesChanged += UiSettingsForSystemTheme_ColorValuesChanged;

        }

        catch

        {

            // ignore

        }

    }



    private void UiSettingsForSystemTheme_ColorValuesChanged(UISettings sender, object args)

    {

        if (ThemeBridge.NormalizeUiThemeKey(_settingsStore.Get("ui_theme", "dark")) != "system")

        {

            return;

        }



        _ = DispatcherQueue.TryEnqueue(

            DispatcherQueuePriority.Normal,

            () => _ = ApplyUiThemeWithShellSoftTransitionAsync(ThemeBridge.NormalizeUiThemeKey("system")));

    }



    private void MainPage_Loaded(object sender, RoutedEventArgs e)

    {

        try

        {

            ThemeBridge.ApplyFromUiThemeKey(_settingsStore.Get("ui_theme", "dark"));

        }

        catch

        {

            ThemeBridge.ApplyFromUiThemeKey("dark");

        }

        GamesTable.RefreshThemeVisuals();
        GamesTable.RefreshColumnLayout();
        _ = ViewModel.RefreshBackupSizeDisplaysAsync();

        HookSystemThemeColorListener();

        BackupBulkTeachingTip.Target = BackupButton;
        CompressWorkflowTeachingTip.Target = CompressButton;
        WireBackupTeachingTip();
        WireGameTableContextMenu();
        _ = ScheduleOnboardingTeachingTipAsync();
        _ = MaybeReplayCheckpointBulkBackupTipAsync();

        UpdateScanButtonText();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (App.MainWindowRef is Window shellWindow)
        {
            MainWindowShellProgress.CaptureBaseTitle(shellWindow);
            shellWindow.Activated -= MainWindow_Activated;
            shellWindow.Activated += MainWindow_Activated;
            ApplyMainWindowTitleBarTheme();
        }

        InitializeBottomOverlayLayers();
        SyncBackupIntegrityStripUi();
        ConfigureScanMenuForSimulation();

        InitializeMainCommandBarChrome();
        GsbtMenuFlyoutChrome.ApplyToFlyout(ScanMenuFlyout);
        GsbtMenuFlyoutChrome.ApplyToFlyout(ToolsMenuFlyout);
        ApplyMainWindowTitleBarTheme();

        MonitorCommandButton.Visibility =
            App.LaunchSandboxMonitor && !App.IsSandboxSimulationChild ? Visibility.Visible : Visibility.Collapsed;

        RequestFooterCommandBarOverflowRelayout();

        FooterCommandRow.SizeChanged += FooterCommandRow_SizeChanged;

        DispatcherQueue.TryEnqueue(

            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,

            () =>

            {

                if (FooterCommandRow.ActualWidth > 0)

                {

                    UpdateFooterCommandBarOverflow(FooterCommandRow.ActualWidth);

                }

            });

        if (App.LaunchSandboxMonitor && !App.IsSandboxSimulationChild)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                OpenSandboxMonitor);
        }

        StartSimulationIpcListenerIfChild();

        if (App.MainWindowRef is Window hostWindow)

        {

            hostWindow.SizeChanged -= MainWindow_SizeChanged;

            hostWindow.SizeChanged += MainWindow_SizeChanged;

        }

        TryAttachMainWindowClosing();



        ViewModel.DisplayedGamesRebuildStarting += ViewModel_DisplayedGamesRebuildStarting;
        ViewModel.DisplayedListRebuilt += ViewModel_DisplayedListRebuilt;

        SyncProgressTrackOpacity();

        SettingsPageButton.Click += SettingsPageButton_Fallback_Click;
        GamesTable.RowsListView.DoubleTapped += GamesGrid_DoubleTapped;
        GamesGrid.SelectionChanged += GamesGrid_SelectionChanged;



        DispatcherQueue.TryEnqueue(

            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,

            () =>

            {

                try

                {

                    _trayService.Initialize(

                        App.MainWindowRef!,

                        DispatcherQueue,

                        ViewModel,

                        onShow: async () =>

                        {

                            ViewModel.ReconcileLastBackupDiskIntegrity();

                            if (App.MainWindowRef is Window win)

                            {

                                MainWindowTrayVisibility.ShowAndActivate(win);

                            }

                            await Task.CompletedTask;

                        },

                        onBackup: async () => await RunManualBackupAsync(fromTray: true),

                        onCompress: async () => await CompressBackupFolderFromUiAsync(),

                        onQuit: async () =>

                        {

                            ForceQuitAndCloseMainWindow();

                            await Task.CompletedTask;

                        },

                        App.IsSandboxSimulationChild);

                }

                catch

                {

                    ViewModel.StatusText = "Tray icon unavailable.";

                }

            });

    }



    private async Task MaybeReplayCheckpointBulkBackupTipAsync()
    {
        await Task.Delay(800);
        if (!ViewModel.HasPendingReplayCheckpointBulkBackup())
        {
            return;
        }

        if (ViewModel.Games.Count == 0)
        {
            return;
        }

        ViewModel.TryInvokeBackupBulkTeachingTipIfDue();
    }



    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        ApplyMainWindowTitleBarTheme();
    }

    private void ApplyMainWindowTitleBarTheme()
    {
        try
        {
            if (App.MainWindowRef is not Window window)
            {
                return;
            }

            var theme = RequestedTheme is ElementTheme.Light or ElementTheme.Dark
                ? RequestedTheme
                : ActualTheme;
            TitleBarThemeHelper.Apply(window, theme);
        }
        catch
        {
            // ignore
        }
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)

    {

        ActualThemeChanged -= MainPage_ActualThemeChanged;

        if (_uiSettingsForSystemTheme is not null)

        {

            _uiSettingsForSystemTheme.ColorValuesChanged -= UiSettingsForSystemTheme_ColorValuesChanged;

        }

        CancelProgressHideTimer();
        await CleanupStatusToastOnUnloadAsync();

        FooterCommandRow.SizeChanged -= FooterCommandRow_SizeChanged;

        if (App.MainWindowRef is Window hostWindow)

        {

            hostWindow.SizeChanged -= MainWindow_SizeChanged;
            hostWindow.Activated -= MainWindow_Activated;

        }

        TryDetachMainWindowClosing();

        if (_mainWindowResizePersistTimer is not null)

        {

            _mainWindowResizePersistTimer.Stop();

            _mainWindowResizePersistTimer.Tick -= MainWindowResizePersistTimer_Tick;

            _mainWindowResizePersistTimer = null;

        }

        SettingsPageButton.Click -= SettingsPageButton_Fallback_Click;
        GamesTable.RowsListView.DoubleTapped -= GamesGrid_DoubleTapped;
        GamesGrid.SelectionChanged -= GamesGrid_SelectionChanged;

        ViewModel.DisplayedGamesRebuildStarting -= ViewModel_DisplayedGamesRebuildStarting;
        ViewModel.DisplayedListRebuilt -= ViewModel_DisplayedListRebuilt;

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.NotifyAutoBackupTip = null;
        UnwireBackupTeachingTip();
        UnwireGameTableContextMenu();
        MainWindowShellProgress.Clear(App.MainWindowRef);

    }

    private void SyncShellProgressChrome()
    {
        MainWindowShellProgress.Sync(App.MainWindowRef, ViewModel);
    }



    private void ViewModel_DisplayedGamesRebuildStarting(object? sender, EventArgs e) =>
        _suppressGamesSelectionSync = true;



    private void ViewModel_DisplayedListRebuilt(object? sender, EventArgs e)

    {

        try

        {

            GamesGrid.SelectedItems.Clear();

            foreach (var row in ViewModel.DisplayedGames)

            {

                if (ViewModel.IsLogicallySelected(row))

                {

                    GamesGrid.SelectedItems.Add(row);

                }

            }

        }

        finally

        {

            _suppressGamesSelectionSync = false;

        }

    }



    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (_suppressGamesSelectionSync)

        {

            return;

        }

        ViewModel.SyncLogicalSelectionFromVisibleGrid(GamesGrid.SelectedItems);

    }



    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)

    {

        if (e.PropertyName == nameof(MainViewModel.IsProgressStripVisible))

        {

            AnimateProgressTrackOpacity(ViewModel.IsProgressStripVisible);

        }

        if (e.PropertyName is nameof(MainViewModel.IsScanning) or nameof(MainViewModel.IsBusy))

        {

            OnScanBusyStateChanged();

        }

        if (e.PropertyName is nameof(MainViewModel.FooterBackupShowsCancel)
            or nameof(MainViewModel.FooterCompressShowsCancel))

        {

            RefreshBackupCompressFooterChrome();

        }

        if (e.PropertyName is nameof(MainViewModel.ScanProgress)
            or nameof(MainViewModel.IsBusy)
            or nameof(MainViewModel.FooterBackupShowsCancel)
            or nameof(MainViewModel.FooterCompressShowsCancel))

        {

            SyncShellProgressChrome();

        }

        if (e.PropertyName == nameof(MainViewModel.FilterButtonText))

        {

            RequestFooterCommandBarOverflowRelayout();

        }

        if (e.PropertyName is nameof(MainViewModel.BackupIntegrityStripVisible)
            or nameof(MainViewModel.BackupIntegrityStripMessage))
        {
            SyncBackupIntegrityStripUi();
        }

    }

    private void BackupIntegrityStripDismiss_Click(object sender, RoutedEventArgs e) =>
        ViewModel.DismissBackupIntegrityStrip();

    private void SyncBackupIntegrityStripUi()
    {
        var shouldShow = ViewModel.BackupIntegrityStripVisible
            && !_settingsOpen
            && _settingsStore.Get("in_app_backup_warnings_enabled", true);
        BackupIntegrityStripText.Text = ViewModel.BackupIntegrityStripMessage ?? string.Empty;

        if (shouldShow == _integrityStripUiShown)
        {
            return;
        }

        if (shouldShow)
        {
            _integrityStripUiShown = true;
            BackupIntegrityStripGlyph.Visibility = Visibility.Visible;
            BackupIntegrityStripBorder.Visibility = Visibility.Visible;
            BackupIntegrityStripBorder.Opacity = 0;
            BackupIntegrityStripTransform.Y = 8;
            BuildIntegrityStripStoryboard(showing: true).Begin();
        }
        else
        {
            _integrityStripUiShown = false;
            BackupIntegrityStripGlyph.Visibility = Visibility.Collapsed;
            var sb = BuildIntegrityStripStoryboard(showing: false);
            void OnHideDone(object? s, object e)
            {
                sb.Completed -= OnHideDone;
                BackupIntegrityStripBorder.Visibility = Visibility.Collapsed;
                BackupIntegrityStripBorder.Opacity = 0;
            }

            sb.Completed += OnHideDone;
            sb.Begin();
        }
    }

    private Storyboard BuildIntegrityStripStoryboard(bool showing)
    {
        var sb = new Storyboard();
        var opacity = new DoubleAnimation
        {
            From = showing ? 0 : BackupIntegrityStripBorder.Opacity,
            To = showing ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(showing ? 160 : 220)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(opacity, BackupIntegrityStripBorder);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        sb.Children.Add(opacity);

        var translate = new DoubleAnimation
        {
            From = showing ? 8 : 0,
            To = showing ? 0 : 8,
            Duration = new Duration(TimeSpan.FromMilliseconds(showing ? 160 : 220)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(translate, BackupIntegrityStripTransform);
        Storyboard.SetTargetProperty(translate, "Y");
        sb.Children.Add(translate);

        return sb;
    }



    private void SyncProgressTrackOpacity()

    {

        ProgressTrack.Opacity = ViewModel.IsProgressStripVisible ? 1 : 0;

    }



    private void AnimateProgressTrackOpacity(bool visible)

    {

        var anim = new DoubleAnimation

        {

            From = ProgressTrack.Opacity,

            To = visible ? 1 : 0,

            Duration = new Duration(TimeSpan.FromMilliseconds(180)),

            EnableDependentAnimation = true

        };

        var sb = new Storyboard();

        Storyboard.SetTarget(anim, ProgressTrack);

        Storyboard.SetTargetProperty(anim, "Opacity");

        sb.Children.Add(anim);

        sb.Begin();

    }



    private void OnScanBusyStateChanged()

    {

        if (ViewModel.IsScanning || ViewModel.IsBusy)

        {

            CancelProgressHideTimer();

            return;

        }



        if (ViewModel.ScanProgress <= 0.5)

        {

            return;

        }



        RestartProgressHideTimer();

    }



    private void RestartProgressHideTimer()

    {

        CancelProgressHideTimer();

        _progressHideCts = new CancellationTokenSource();

        var token = _progressHideCts.Token;

        _ = HideProgressStripAfterDelayAsync(token);

    }



    private async Task HideProgressStripAfterDelayAsync(CancellationToken token)

    {

        try

        {

            await Task.Delay(TimeSpan.FromSeconds(5), token);

            if (token.IsCancellationRequested || ViewModel.IsScanning || ViewModel.IsBusy)

            {

                return;

            }



            DispatcherQueue.TryEnqueue(() => ViewModel.ScanProgress = 0);

        }

        catch (TaskCanceledException)

        {

            // ignore

        }

    }



    private void CancelProgressHideTimer()

    {

        if (_progressHideCts is null)

        {

            return;

        }



        _progressHideCts.Cancel();

        _progressHideCts.Dispose();

        _progressHideCts = null;

    }



    private void GamesGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)

    {

        var rowVm = FindRowViewModel(e.OriginalSource as DependencyObject)

            ?? (GamesGrid.SelectedItem as GameRowViewModel);



        if (rowVm is null)

        {

            return;

        }



        TryOpenSaveFolder(rowVm);

    }



    private static GameRowViewModel? FindRowViewModel(DependencyObject? src)

    {

        while (src != null)

        {

            if (src is FrameworkElement fe && fe.DataContext is GameRowViewModel g)

            {

                return g;

            }



            src = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(src);

        }



        return null;

    }



    private static void TryOpenSaveFolder(GameRowViewModel row)

    {

        try

        {

            var path = row.SavePathResolved;

            if (string.IsNullOrWhiteSpace(path) || row.SaveInRegistryOnly)

            {

                return;

            }



            if (!Directory.Exists(path))

            {

                return;

            }



            Process.Start(new ProcessStartInfo

            {

                FileName = path,

                UseShellExecute = true

            });

        }

        catch

        {

            // Explorer launch failures are non-fatal

        }

    }



    private void ScanFlyoutAnchor_Click(object sender, RoutedEventArgs e)
    {
        ScanMenuFlyout.ShowAt(
            ScanPrimaryButton,
            new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
            });
    }



    private async void ScanPrimary_Click(object sender, RoutedEventArgs e)

    {

        if (ViewModel.IsScanning)

        {

            return;

        }



        UpdateScanButtonText(scanning: true);
        _ = ShowStatusToastAsync("Scanning for installed games...");

        await ViewModel.StartScanAsync();

        UpdateScanButtonText(scanning: false);
        _ = ShowStatusToastAsync(ViewModel.StatusText);

    }



    private async void RefreshManifestAndRescan_Click(object sender, RoutedEventArgs e)

    {

        if (!await ShowManifestRefreshConfirmDialogAsync())
        {
            return;
        }

        UpdateScanButtonText(scanning: true);
        _ = ShowStatusToastAsync("Refreshing manifest and rescanning...");

        await ViewModel.RefreshManifestAndRescanAsync();

        UpdateScanButtonText(scanning: false);
        _ = ShowStatusToastAsync(ViewModel.StatusText);

    }



    private void Filter_Click(object sender, RoutedEventArgs e) => ViewModel.CycleFilter();



    private async void Settings_Click(object sender, RoutedEventArgs e)

    {

        // Guard against stale state where settings was marked open but never became visible.
        if (_settingsOpen && SettingsPanelContainer.Visibility != Visibility.Visible)

        {

            _settingsOpen = false;

        }



        await EnterSettingsAsync();

    }

    private async void SettingsPageButton_Fallback_Click(object sender, RoutedEventArgs e)

    {

        // XAML Click should normally invoke Settings_Click; keep a direct fallback for resilience.
        if (_settingsOpen)

        {

            return;

        }



        await EnterSettingsAsync();

    }



    private async void BackupSelected_Click(object sender, RoutedEventArgs e)

    {

        if (ViewModel.FooterBackupShowsCancel)

        {

            ViewModel.CancelOperation();

            return;

        }

        await RunManualBackupAsync();

    }



    private async void Compress_Click(object sender, RoutedEventArgs e) => await CompressBackupFolderFromUiAsync();

    private async Task CompressBackupFolderFromUiAsync()
    {
        if (ViewModel.FooterCompressShowsCancel)
        {
            ViewModel.CancelOperation();
            return;
        }

        if (!ViewModel.CanUseBackupAndCompress)
        {
            return;
        }

        var selected = ViewModel.SnapshotLogicalSelection();
        var result = await ViewModel.CompressBackupFolderAsync(selected);
        _ = ShowStatusToastAsync(result);
        OsAppNotifications.TryShow(_settingsStore, "Compress", result);
    }



    private void Quit_Click(object sender, RoutedEventArgs e) => _ = HandleUserCloseRequestAsync();



    private async void AddCustomGame_Click(object sender, RoutedEventArgs e)

    {

        var (ok, gameName, mode, saveFolder, registryRaw) = await ShowAddCustomGameDialogAsync();

        if (!ok)

        {

            return;

        }

        var (added, message) = mode == CustomGameDialogSaveMode.Registry
            ? await ViewModel.TryAddCustomGameWithRegistryAsync(gameName, registryRaw)
            : await ViewModel.TryAddCustomGameAsync(gameName, saveFolder);

        if (!added)

        {

            _ = ShowStatusToastAsync(message);

            return;

        }

        _ = ShowStatusToastAsync(message);
        RequestFooterCommandBarOverflowRelayout();

    }



    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await ShowDiagnosticsAsync();

    private void DiagnosticsShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowDiagnosticsAsync();
    }

    private async void About_Click(object sender, RoutedEventArgs e) => await ShowAboutDialogAsync();

    private void AboutShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowAboutDialogAsync();
    }

    private async Task ShowDiagnosticsAsync()
    {
        await ExecuteExclusiveContentDialogAsync(async () =>
        {
            var catalog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSBT", "game_save_data.json");

            string settingsPath;
            try
            {
                settingsPath = App.Host!.Services.GetRequiredService<SettingsStore>().SettingsFilePath;
            }
            catch
            {
                settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSBT", "winui_settings.json");
            }

            var crashLog = AppPaths.WinUiCrashLogPath;
            var rid = RuntimeInformation.ProcessArchitecture.ToString();

            var body =
                $"OS: {Environment.OSVersion}\n" +
                $"Process: {rid}\n" +
                $"Framework: {RuntimeInformation.FrameworkDescription}\n" +
                $"Catalog: {catalog}\n" +
                $"WinUI settings: {settingsPath}\n" +
                $"Crash log: {crashLog}\n" +
                $"Base dir: {AppContext.BaseDirectory}\n";

            var bodyFg = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtBodyTextBrush");
            var valueFg = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtSecondaryLabelBrush");
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Padding = new Thickness(12, 4, 12, 10),
            };
            foreach (var raw in body.Split('\n'))
            {
                var trimmed = raw.TrimEnd();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                var idx = trimmed.IndexOf(':');
                if (idx > 0 && idx < trimmed.Length - 1)
                {
                    var label = trimmed[..idx].TrimEnd();
                    var value = trimmed[(idx + 1)..].TrimStart();
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWholeWords,
                        Margin = new Thickness(0, 0, 0, 4),
                        IsTextSelectionEnabled = true,
                    };
                    tb.Inlines.Add(new Run { Text = label + ":", FontWeight = FontWeights.SemiBold, Foreground = bodyFg });
                    tb.Inlines.Add(new Run { Text = " " + value, Foreground = valueFg });
                    panel.Children.Add(tb);
                }
                else
                {
                    panel.Children.Add(
                        new TextBlock
                        {
                            Text = trimmed,
                            TextWrapping = TextWrapping.WrapWholeWords,
                            Margin = new Thickness(0, 0, 0, 4),
                            IsTextSelectionEnabled = true,
                            Foreground = bodyFg,
                        });
                }
            }

            var scroll = new ScrollViewer
            {
                MaxHeight = ComputeHelpDialogScrollMaxHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel,
            };

            var diag = new ContentDialog
            {
                Title = "Diagnostics",
                Content = scroll,
                CloseButtonText = "Close",
            };
            ApplyShellThemeToContentDialog(diag);
            await GsbtContentDialog.ShowAsync(diag);
        });
    }

    private async Task ShowAboutDialogAsync()
    {
        await ExecuteExclusiveContentDialogAsync(async () =>
        {
            var muted = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtSecondaryLabelBrush");
            var bodyFg = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtBodyTextBrush");

            var root = new StackPanel { Spacing = 10, MaxWidth = 440 };

            var heading = new TextBlock
            {
                Text = AppAboutInfo.AppName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = bodyFg,
            };
            root.Children.Add(heading);
            root.Children.Add(
                new TextBlock
                {
                    Text = AppAboutInfo.VersionDisplay,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Foreground = bodyFg,
                });
            root.Children.Add(
                new TextBlock
                {
                    Text = AppAboutInfo.CopyrightLine,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = muted,
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                });
            root.Children.Add(
                new TextBlock
                {
                    Text = AppAboutInfo.MadeWithLine,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = muted,
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                });
            var linkLine = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsTextSelectionEnabled = true,
                Foreground = bodyFg,
            };
            var gh = new Hyperlink { NavigateUri = new Uri(AppAboutInfo.SourceRepositoryUrl) };
            gh.Inlines.Add(new Run { Text = "Source code on GitHub" });
            linkLine.Inlines.Add(gh);
            root.Children.Add(linkLine);
            root.Children.Add(
                new TextBlock
                {
                    Text = AppAboutInfo.DistributionNote,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = muted,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8, 4, 8, 0),
                });

            var scroll = new ScrollViewer
            {
                Content = root,
                MaxHeight = ComputeHelpDialogScrollMaxHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var aboutDialog = new ContentDialog
            {
                Title = null,
                Content = scroll,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };
            ApplyShellThemeToContentDialog(aboutDialog);
            await GsbtContentDialog.ShowAsync(aboutDialog);
        });
    }



    private void MonitorCommandButton_Click(object sender, RoutedEventArgs e) => OpenSandboxMonitor();

    private void LayoutSandboxMonitorBesideMainWindow()
    {
        if (_sandboxWindow is null)
        {
            return;
        }

        var main = App.MainWindowRef;
        if (main is null)
        {
            WindowSizeHelper.SetClientSize(_sandboxWindow, 720, 520);
            return;
        }

        if (WindowSizeHelper.TryGetClientSize(main, out var w, out var h))
        {
            WindowSizeHelper.SetClientSize(_sandboxWindow, w, h);
        }

        WindowPlacementHelper.PlaceWindowToRightOfOwner(_sandboxWindow, main);
    }

    private void OpenSandboxMonitor()
    {
        if (App.IsSandboxSimulationChild)
        {
            return;
        }

        var hub = App.Host!.Services.GetRequiredService<SandboxLogHub>();

        var simulation = App.Host!.Services.GetRequiredService<SandboxSimulationState>();

        if (_sandboxWindow is null)
        {
            var resourceMonitor = App.Host!.Services.GetRequiredService<SandboxResourceMonitor>();
            _sandboxWindow = new SandboxMonitorWindow(hub, simulation, resourceMonitor);

            _sandboxWindow.SuppressSyncCloseMain = false;

            _sandboxWindow.Closed += (_, _) => _sandboxWindow = null;
        }

        LayoutSandboxMonitorBesideMainWindow();

        _sandboxWindow.Activate();

        SyncSandboxMonitorChromeTheme();

    }



    internal void SyncSandboxMonitorChromeTheme(ElementTheme resolvedLightOrDark)
    {
        try
        {
            if (_sandboxWindow is null)
            {
                return;
            }

            _sandboxWindow.ApplyShellChromeTheme(resolvedLightOrDark);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Uses the same resolution rules as shell chrome: prefer explicit <see cref="RequestedTheme"/>, then <see cref="ActualTheme"/>.</summary>
    internal void SyncSandboxMonitorChromeTheme() =>
        SyncSandboxMonitorChromeTheme(ResolveMonitorTargetThemeFromMainPage());

    private ElementTheme ResolveMonitorTargetThemeFromMainPage() =>
        RequestedTheme is ElementTheme.Light or ElementTheme.Dark
            ? RequestedTheme
            : (ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);



    private void SelectAll_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)

    {

        _suppressGamesSelectionSync = true;

        try

        {

            GamesGrid.SelectedItems.Clear();

            foreach (var row in ViewModel.DisplayedGames)

            {

                GamesGrid.SelectedItems.Add(row);

            }

            ViewModel.AlignLogicalSelectionTo(ViewModel.DisplayedGames);

        }

        finally

        {

            _suppressGamesSelectionSync = false;

        }



        args.Handled = true;

    }



    private void BackupShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)

    {

        if (!ViewModel.BackupFooterEnabled)

        {

            args.Handled = true;

            return;

        }

        BackupSelected_Click(this, new RoutedEventArgs());

        args.Handled = true;

    }



    private void DeleteRows_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)

    {

        var selected = ViewModel.SnapshotLogicalSelection();

        ViewModel.RemoveRows(selected);

        args.Handled = true;

    }



    private void UndoDelete_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)

    {

        ViewModel.UndoDelete();

        args.Handled = true;

    }



    private void ShowShortcuts_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowShortcutsDialogAsync();
    }

    private async void ShortcutsMenu_Click(object sender, RoutedEventArgs e) => await ShowShortcutsDialogAsync();

    private async Task ExecuteExclusiveContentDialogAsync(Func<Task> showAsync)
    {
        if (XamlRoot is null)
        {
            return;
        }

        if (!await _contentDialogMutex.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await showAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            try
            {
                ViewModel.StatusText = $"Dialog: {ex.Message}";
            }
            catch
            {
                // ignore
            }
        }
        finally
        {
            try
            {
                _contentDialogMutex.Release();
            }
            catch
            {
                // ignore double-release
            }
        }
    }

    private double ComputeHelpDialogScrollMaxHeight()
    {
        var h = ActualHeight;
        if (h <= 0 || double.IsNaN(h))
        {
            return 420;
        }

        return Math.Clamp(h * 0.62, 220, 560);
    }

    /// <summary>Fluent modal chrome follows <see cref="MainPage"/> so dialogs match light/dark/system with the rest of the shell.</summary>
    private void ApplyShellThemeToContentDialog(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.RequestedTheme = ActualTheme;
    }

    private static void AppendShortcutLine(StackPanel panel, string boldKeys, string description, Brush keyBrush, Brush descBrush)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8),
            IsTextSelectionEnabled = true,
        };
        tb.Inlines.Add(new Run { Text = boldKeys, FontWeight = FontWeights.SemiBold, Foreground = keyBrush });
        tb.Inlines.Add(new Run { Text = ": " + description, Foreground = descBrush });
        panel.Children.Add(tb);
    }

    private ScrollViewer BuildShortcutsHelpScrollViewer(double maxScrollHeight)
    {
        var shellDark = ThemeBridge.IsShellDarkTheme();
        var keyBrush = ThemeBridge.GetGsbtBrush(shellDark, "GsbtBodyTextBrush");
        var descBrush = ThemeBridge.GetGsbtBrush(shellDark, "GsbtSecondaryLabelBrush");
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Padding = new Thickness(12, 4, 12, 10),
        };
        void Line(string keys, string desc) => AppendShortcutLine(panel, keys, desc, keyBrush, descBrush);

        Line("Ctrl+A", "select all visible rows");
        Line("Delete", "remove selected rows");
        Line("Ctrl+Z", "undo last delete");
        Line("Ctrl+B", "back up selected games (or all with save folders)");
        Line("F1", "open Shortcuts (this panel)");
        Line("F2", "open Settings");
        Line("F11", "open About");
        Line("F12", "open Diagnostics");
        Line("Ctrl+click", "add or remove a row from the selection");
        Line("Shift+click", "select a contiguous range from the anchor row");
        Line("Arrow keys", "move row focus; Shift+arrows extend selection");
        Line("Click empty list area", "below rows or padding clears selection");
        Line("Drag on empty list area", "rubber-band select rows (release with Ctrl held to add to selection)");
        Line("Double-click a row", "open save folder in Explorer when the path exists");
        Line("Shift+F10", "open row context menu (Backup, Delete, open folders, and related actions)");
        Line("Escape", "cancel backup or compress when Cancel is shown; dismiss status toasts; otherwise close Settings");

        return new ScrollViewer
        {
            MaxHeight = maxScrollHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
        };
    }

    private Task ShowShortcutsDialogAsync() =>
        ExecuteExclusiveContentDialogAsync(async () =>
        {
            var tips = new ContentDialog
            {
                Title = "Shortcuts",
                Content = BuildShortcutsHelpScrollViewer(ComputeHelpDialogScrollMaxHeight()),
                CloseButtonText = "Close",
            };
            ApplyShellThemeToContentDialog(tips);
            await GsbtContentDialog.ShowAsync(tips);
        });

    private async void OpenSettingsShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)

    {

        args.Handled = true;

        await EnterSettingsAsync();

    }

    private void TryAttachMainWindowClosing()
    {
        TryDetachMainWindowClosing();
        try
        {
            if (App.MainWindowRef is not Window w)
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(w);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _mainAppWindow = AppWindow.GetFromWindowId(windowId);
            _mainAppWindow.Closing += MainWindow_AppWindowClosing;
        }
        catch
        {
            _mainAppWindow = null;
        }
    }

    private void TryDetachMainWindowClosing()
    {
        if (_mainAppWindow is null)
        {
            return;
        }

        try
        {
            _mainAppWindow.Closing -= MainWindow_AppWindowClosing;
        }
        catch
        {
            // ignore
        }

        _mainAppWindow = null;
    }

    private bool _deferredMainWindowCloseScheduled;

    private void MainWindow_AppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceMainWindowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_deferredMainWindowCloseScheduled)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            () => _ = HandleUserCloseRequestAsync());
    }

    /// <summary>Footer Close and title-bar X share tray minimize and compress-before-exit settings.</summary>
    private async Task HandleUserCloseRequestAsync()
    {
        if (_forceMainWindowClose)
        {
            return;
        }

        try
        {
            if (_settingsStore.Get("minimize_to_tray", false) && _trayService.IsTrayAvailable)
            {
                if (App.MainWindowRef is Window w)
                {
                    MainWindowTrayVisibility.Hide(w);
                }

                OsAppNotifications.TryShow(
                    _settingsStore,
                    string.Empty,
                    "Application was minimized to tray");
                return;
            }
        }
        catch
        {
            // fall through to deferred exit
        }

        if (_deferredMainWindowCloseScheduled)
        {
            return;
        }

        _deferredMainWindowCloseScheduled = true;
        try
        {
            await RunDeferredApplicationExitAsync().ConfigureAwait(true);
        }
        finally
        {
            _deferredMainWindowCloseScheduled = false;
        }
    }

    private void CloseSandboxMonitorForMainAppExit()
    {
        try
        {
            if (_sandboxWindow is null)
            {
                return;
            }

            _sandboxWindow.SuppressSyncCloseMain = true;
            _sandboxWindow.Close();
        }
        catch
        {
        }
    }

    private async Task<bool> TryPrepareSandboxMonitorClosureBeforeMainExitAsync()
    {
        if (!App.LaunchSandboxMonitor)
        {
            return true;
        }

        if (!_settingsStore.Get("sandbox_close_monitor_when_main_closes", false))
        {
            return true;
        }

        if (_sandboxWindow is null)
        {
            return true;
        }

        var session = App.Host!.Services.GetRequiredService<SandboxMonitorSession>();

        if (XamlRoot is null)
        {
            CloseSandboxMonitorForMainAppExit();
            return true;
        }

        if (session.IsBatchBenchmarkRunning)
        {
            var urgent = new ContentDialog
            {
                Title = "Batch benchmark running",
                PrimaryButtonText = "Close monitor and exit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Text =
                        "A sandbox batch compression is still running. Exiting will stop that work and close the Sandbox Monitor. "
                        + "This warning always appears while a batch is running, even if you turned on \"don't ask again\" for normal exits.",
                },
            };
            ApplyShellThemeToContentDialog(urgent);
            var ur = await GsbtContentDialog.ShowAsync(urgent);
            if (ur != ContentDialogResult.Primary)
            {
                return false;
            }

            CloseSandboxMonitorForMainAppExit();
            return true;
        }

        if (_settingsStore.Get("sandbox_close_monitor_when_main_closes_skip_confirm", false))
        {
            CloseSandboxMonitorForMainAppExit();
            return true;
        }

        var dontAsk = new CheckBox { Content = "Don't ask again when no batch is running", IsChecked = false };
        var sp = new StackPanel { Spacing = 10 };
        sp.Children.Add(
            new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text = "Close the Sandbox Monitor when the main window closes?",
            });
        sp.Children.Add(dontAsk);
        var ask = new ContentDialog
        {
            Title = "Close Sandbox Monitor?",
            Content = sp,
            PrimaryButtonText = "Close monitor and exit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        ApplyShellThemeToContentDialog(ask);
        var choice = await GsbtContentDialog.ShowAsync(ask);
        if (choice != ContentDialogResult.Primary)
        {
            return false;
        }

        if (dontAsk.IsChecked == true)
        {
            _settingsStore.Set("sandbox_close_monitor_when_main_closes_skip_confirm", true);
        }

        CloseSandboxMonitorForMainAppExit();
        return true;
    }

    private async Task RunDeferredApplicationExitAsync()
    {
        try
        {
            await _contentDialogMutex.WaitAsync().ConfigureAwait(true);
            try
            {
                if (!await TryPrepareSandboxMonitorClosureBeforeMainExitAsync().ConfigureAwait(true))
                {
                    return;
                }

                if (XamlRoot is null)
                {
                    ForceQuitAndCloseMainWindow();
                    return;
                }

                var backupRoot = ViewModel.GetEffectiveBackupRootForCompressPrompt();
                if (_settingsStore.Get("ask_compress_on_exit", false)
                    && !string.IsNullOrWhiteSpace(backupRoot)
                    && Directory.Exists(backupRoot))
                {
                    var ask = new ContentDialog
                    {
                        Title = "Compress backups?",
                        PrimaryButtonText = "Yes",
                        SecondaryButtonText = "No",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Secondary,
                        Content = new TextBlock
                        {
                            TextWrapping = TextWrapping.WrapWholeWords,
                            IsTextSelectionEnabled = true,
                            Text =
                                "Would you like to compress your backups before closing?\n\n"
                                + "This creates a single archive inside your backup folder using Settings → Compression (ZIP or 7-Zip). "
                                + "You can compress anytime from the footer Compress control or the tray menu.\n\n"
                                + "Yes: compress then exit if compression succeeds. No: exit without compressing. Cancel: stay in the app.",
                        },
                    };
                    ApplyShellThemeToContentDialog(ask);
                    var choice = await GsbtContentDialog.ShowAsync(ask);
                    if (choice == ContentDialogResult.None)
                    {
                        return;
                    }

                    if (choice == ContentDialogResult.Primary)
                    {
                        var (msg, res) = await ViewModel.CompressBackupFolderWithResultAsync().ConfigureAwait(true);
                        if (res is not { Success: true })
                        {
                            var follow = new ContentDialog
                            {
                                Title = "Compress before exit",
                                CloseButtonText = "Close",
                                Content = new TextBlock
                                {
                                    TextWrapping = TextWrapping.WrapWholeWords,
                                    IsTextSelectionEnabled = true,
                                    Text = string.IsNullOrWhiteSpace(msg)
                                        ? "Compression did not finish successfully. The app will stay open."
                                        : msg,
                                },
                            };
                            ApplyShellThemeToContentDialog(follow);
                            await GsbtContentDialog.ShowAsync(follow);
                            return;
                        }
                    }
                }

                ForceQuitAndCloseMainWindow();
            }
            finally
            {
                try
                {
                    _contentDialogMutex.Release();
                }
                catch
                {
                    // ignore double-release
                }
            }
        }
        catch
        {
            ForceQuitAndCloseMainWindow();
        }
        finally
        {
            _deferredMainWindowCloseScheduled = false;
        }
    }

    private void ForceQuitAndCloseMainWindow()
    {
        App.MarkApplicationExiting();

        if (App.LaunchSandboxMonitor && _sandboxWindow is not null)
        {
            try
            {
                _sandboxWindow.SuppressSyncCloseMain = true;
                _sandboxWindow.Close();
            }
            catch
            {
            }

            _sandboxWindow = null;
        }

        _forceMainWindowClose = true;
        ViewModel.Shutdown();
        _trayService.Dispose();
        App.MainWindowRef?.Close();
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        if (_suppressMainWindowResizePersist)
        {
            return;
        }

        _mainWindowResizePersistTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _mainWindowResizePersistTimer.Stop();
        _mainWindowResizePersistTimer.Tick -= MainWindowResizePersistTimer_Tick;
        _mainWindowResizePersistTimer.Tick += MainWindowResizePersistTimer_Tick;
        _mainWindowResizePersistTimer.Start();
    }

    private void MainWindowResizePersistTimer_Tick(object? sender, object e)
    {
        if (_mainWindowResizePersistTimer is not null)
        {
            _mainWindowResizePersistTimer.Stop();
            _mainWindowResizePersistTimer.Tick -= MainWindowResizePersistTimer_Tick;
        }

        if (_suppressMainWindowResizePersist)
        {
            return;
        }

        if (_settingsStore.Get("main_window_lock_resolution", false))
        {
            return;
        }

        var w = App.MainWindowRef;
        if (w is null || !WindowSizeHelper.TryGetClientSize(w, out var ww, out var hh))
        {
            return;
        }

        ViewModel.PersistMainWindowClientResize(ww, hh);
    }

    /// <summary>Re-applies stored client size after settings save; suppresses resize→custom persistence for this apply.</summary>
    private void ApplyMainWindowSizeFromSettingsWithSuppress()
    {
        var w = App.MainWindowRef;
        if (w is null)
        {
            return;
        }

        _suppressMainWindowResizePersist = true;
        WindowSizeHelper.ApplyMainWindowFromSettings(_settingsStore, w);
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { _suppressMainWindowResizePersist = false; });
    }

    private void InitializeBottomOverlayLayers()
    {
        Canvas.SetZIndex(MainContentArea, 0);
        Canvas.SetZIndex(BottomMessageStack, 1);
        Canvas.SetZIndex(SettingsPanelContainer, 2);
    }

    private void UpdateScanButtonText(bool? scanning = null)

    {

        var active = scanning ?? ViewModel.IsScanning;

        var text = active ? "Scanning…" : "Scan for games";
        if (_scanCommandBarLabel is not null)
        {
            _scanCommandBarLabel.Text = text;
        }

        RequestFooterCommandBarOverflowRelayout();

    }

}

