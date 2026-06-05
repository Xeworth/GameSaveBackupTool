using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GSBT.Core.Services;
using GSBT.WinUI;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;

namespace GSBT.WinUI.Views;

/// <summary>
/// Settings tab pages hosted in the main window (footer tabs + OK/Cancel live on <see cref="MainPage"/>).
/// </summary>
public sealed partial class SettingsPanel : UserControl
{
    private const double CompactFont = 11;
    private const double TitleFont = 12;
    private const double CardCornerRadius = 8;
    private const double CardInnerSpacing = 7;
    /// <summary>Single-line inputs aligned with compact toolbar buttons.</summary>
    private static double SettingsControlHeight => UiMetrics.CommandBarButtonMinHeight;
    /// <summary>Narrow column so labels and controls stay close on wide windows; centered with large side inset.</summary>
    private const double SettingsTabMaxWidth = 640;
    private const double SettingsHorizontalInset = 16;
    /// <summary>Fixed width for every settings card (matches widest compression card at minimum window).</summary>
    private const double SettingsCardFixedWidth = 568;
    /// <summary>Max width for combo/number value cells so long items stay inside the card; pairs with an Auto value column.</summary>
    private const double SettingsIntrinsicValueMaxWidth =
        SettingsCardFixedWidth - (10 + 10) - 160 - 10; // card L/R padding, label min, label–value gap
    /// <summary>Combos on System tab (popup alignment uses fixed Width); half of intrinsic max plus room for AM/PM captions.</summary>
    private const double SettingsSystemComboMaxWidth =
        SettingsIntrinsicValueMaxWidth / 2 + 40;
    /// <summary>Very short captions — one third of intrinsic max.</summary>
    private const double SettingsSystemStatusDurationComboMaxWidth =
        SettingsIntrinsicValueMaxWidth / 3;
    /// <summary>Fixed width for the right column on the Compression tab (preset + Get 7-Zip share one cell).</summary>
    private const double CompressionTabInputColumnWidth = SettingsIntrinsicValueMaxWidth;

    private readonly MainViewModel _vm;
    private readonly Action? _afterLiveThemeApply;
    private readonly Func<string, Task>? _liveThemeApplyAsync;
    private bool _suppressThemeComboEvents;
    private readonly List<Border> _settingsCardBorders = new();
    private Grid? _settingsRootGrid;
    private readonly List<(TextBlock Text, string BrushKey)> _themedForegroundTextBlocks = new();
    private readonly FrameworkElement[] _tabPages = new FrameworkElement[3];
    private readonly SemaphoreSlim _settingsTabTransitionLock = new(1, 1);
    private int _selectedTab;
    private bool _suppressMainWindowLivePreview;

    private CheckBox _autoBackupCheck = null!;
    private NumberBox _frequencyBox = null!;
    private NumberBox _retentionBox = null!;
    private CheckBox _subfolderCheck = null!;
    private TextBlock _defaultBackupPathDisplay = null!;
    private string _defaultBackupPathForSave = string.Empty;
    private CheckBox _backupEstimateCheck = null!;
    private CheckBox _collisionWarnCheck = null!;
    private CheckBox _showDuplicateTitlesCheck = null!;
    private CheckBox _notificationsCheck = null!;
    private CheckBox _notificationSoundCheck = null!;
    private CheckBox _inAppEphemeralCheck = null!;
    private CheckBox _inAppBackupWarningsCheck = null!;
    private CheckBox _minimizeTrayCheck = null!;
    private CheckBox _showPlatformColumnCheck = null!;
    private CheckBox _showBackupSizeColumnCheck = null!;
    private GsbtSettingsDropdown _themeCombo = null!;
    private GsbtSettingsDropdown _dateFormatCombo = null!;
    private GsbtSettingsDropdown _startupModeCombo = null!;
    private GsbtSettingsDropdown _statusMessageDurationCombo = null!;
    private GsbtSettingsDropdown _mainWindowSizeCombo = null!;
    private CheckBox _lockResolutionCheck = null!;
    private CheckBox _replayTeachingTipsNextLaunchCheck = null!;
    private MainSettingsPayload _baselinePayload = default!;
    private readonly SettingsStore _store;
    private CheckBox _askCompressOnExitCheck = null!;
    private GsbtSettingsDropdown _compressionPresetCombo = null!;
    private GsbtSettingsDropdown _compression7zFormatCombo = null!;
    private NumberBox _compressionMxBox = null!;
    private NumberBox _compressionThreadsBox = null!;
    private string _compression7zPathValue = string.Empty;
    private TextBlock _compression7zPathDisplay = null!;
    private Button _browse7zButton = null!;
    private Button _get7zipButton = null!;
    private Button _sevenZipOfficialSiteButton = null!;
    private Button _sevenZipInfoButton = null!;
    private TeachingTip _sevenZipGetVsWebsiteTeachingTip = null!;
    private TextBlock _sevenZipGetVsWebsiteTeachingTipBody = null!;
    private Grid _compressionEngineRowGrid = null!;
    private StackPanel _sevenZipEngineActionsStrip = null!;
    private TextBlock _sevenZipInstallStatusText = null!;
    private Border _sevenZipOnPcCard = null!;
    private Border _sevenZipExecutableCard = null!;
    private CompressionTabBaseline _compressionBaseline = new(false, CompressionOptionsResolver.PresetDeflateBalanced, "7z", 5, 0, string.Empty);

    public SettingsPanel(MainViewModel viewModel, SettingsStore store, Action? afterLiveThemeApply = null, Func<string, Task>? liveThemeApplyAsync = null)
    {
        _vm = viewModel;
        _store = store;
        _afterLiveThemeApply = afterLiveThemeApply;
        _liveThemeApplyAsync = liveThemeApplyAsync;

        var btnPad = new Thickness(
            UiMetrics.ButtonPaddingHorizontal,
            UiMetrics.ButtonPaddingVerticalCompact,
            UiMetrics.ButtonPaddingHorizontal,
            UiMetrics.ButtonPaddingVerticalCompact);

        var backupPanel = BuildBackupTab(btnPad);
        var compressInner = BuildCompressTab();
        var systemPanel = BuildSystemTab();

        _tabPages[0] = backupPanel;
        var compressTabHost = new Grid();
        compressTabHost.Children.Add(compressInner);
        compressTabHost.Children.Add(_sevenZipGetVsWebsiteTeachingTip);
        Canvas.SetZIndex(_sevenZipGetVsWebsiteTeachingTip, 10);
        _tabPages[1] = compressTabHost;
        _tabPages[2] = systemPanel;

        var contentGrid = new Grid();
        for (var i = 0; i < _tabPages.Length; i++)
        {
            Grid.SetRow(_tabPages[i], 0);
            Canvas.SetZIndex(_tabPages[i], 0);
            _tabPages[i].Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tabPages[i].Opacity = 1;
            contentGrid.Children.Add(_tabPages[i]);
        }

        var tabScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsVerticalRailEnabled = false,
            Content = contentGrid,
        };
        ScrollViewer.SetCanContentRenderOutsideBounds(tabScroll, true);

        var root = new Grid();
        _settingsRootGrid = root;
        root.Background = TryBrush("GsbtWindowBgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(tabScroll, 0);
        root.Children.Add(tabScroll);

        Content = root;

        SelectTab(0);
        ReloadFields();
        Loaded += OnLoadedSyncShellChromeOnce;
    }

    private void OnLoadedSyncShellChromeOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncShellChromeOnce;
        SyncShellThemeFromMainPage();
    }

    /// <summary>Realigns this panel with <see cref="MainPage"/> theme and reapplies code-built Gsbt fills (call right after <see cref="ThemeBridge.ApplyFromUiThemeKey"/>).</summary>
    public void SyncShellThemeFromMainPage()
    {
        try
        {
            if (App.MainWindowRef?.Content is Frame f && f.Content is MainPage mp)
            {
                RequestedTheme = mp.RequestedTheme;
            }
        }
        catch
        {
            // ignore
        }

        RefreshAfterUiThemeChange();
    }

    /// <summary>Card chrome borders in top-to-bottom build order; for staggered theme motion on the main shell.</summary>
    public IReadOnlyList<Border> GetSettingsCardChromeTargetsInOrder() => _settingsCardBorders;

    public int SelectedTab => _selectedTab;

    public void SelectTab(int index)
    {
        index = Math.Clamp(index, 0, _tabPages.Length - 1);
        _selectedTab = index;
        for (var i = 0; i < _tabPages.Length; i++)
        {
            _tabPages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void SelectNextTab() => SelectTab((_selectedTab + 1) % _tabPages.Length);

    public void SelectPreviousTab() => SelectTab((_selectedTab + _tabPages.Length - 1) % _tabPages.Length);

    public Task SelectTabAnimatedAsync(int index)
    {
        index = Math.Clamp(index, 0, _tabPages.Length - 1);
        return SelectTabAnimatedCoreAsync(index);
    }

    public Task SelectNextTabAnimatedAsync() => SelectTabAnimatedAsync((_selectedTab + 1) % _tabPages.Length);

    public Task SelectPreviousTabAnimatedAsync() => SelectTabAnimatedAsync((_selectedTab + _tabPages.Length - 1) % _tabPages.Length);

    private async Task SelectTabAnimatedCoreAsync(int index)
    {
        await _settingsTabTransitionLock.WaitAsync();
        try
        {
            if (index == _selectedTab)
            {
                return;
            }

            var outgoing = _tabPages[_selectedTab];
            var incoming = _tabPages[index];
            _selectedTab = index;

            Canvas.SetZIndex(incoming, 1);
            Canvas.SetZIndex(outgoing, 0);

            incoming.Visibility = Visibility.Visible;
            incoming.Opacity = 0;
            outgoing.Visibility = Visibility.Visible;
            outgoing.Opacity = 1;

            var sb = new Storyboard();
            var outAnim = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(outAnim, outgoing);
            Storyboard.SetTargetProperty(outAnim, "Opacity");
            sb.Children.Add(outAnim);

            var inAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(inAnim, incoming);
            Storyboard.SetTargetProperty(inAnim, "Opacity");
            sb.Children.Add(inAnim);

            var tcs = new TaskCompletionSource();
            void OnComplete(object? s, object e)
            {
                sb.Completed -= OnComplete;
                outgoing.Visibility = Visibility.Collapsed;
                outgoing.Opacity = 1;
                Canvas.SetZIndex(incoming, 0);
                Canvas.SetZIndex(outgoing, 0);
                tcs.TrySetResult();
            }

            sb.Completed += OnComplete;
            sb.Begin();
            await tcs.Task;
        }
        finally
        {
            _settingsTabTransitionLock.Release();
        }
    }

    public void ReloadFields()
    {
        _suppressMainWindowLivePreview = true;
        try
        {
            var s = _vm.LoadSettingsUi();
        _autoBackupCheck.IsChecked = s.AutoBackupEnabled;
        _frequencyBox.Value = s.BackupFrequencyMinutes;
        _retentionBox.Value = s.BackupRetentionCount;
        _subfolderCheck.IsChecked = s.BackupSubfolderPerGame;
        _backupEstimateCheck.IsChecked = s.BackupSizeEstimateEnabled;
        _collisionWarnCheck.IsChecked = s.WarnBackupFolderNameCollisions;
        _defaultBackupPathForSave = s.DefaultBackupPath ?? string.Empty;
        ApplyDefaultBackupPathDisplay();
        _showDuplicateTitlesCheck.IsChecked = s.ShowDuplicateSaveTitles;
        _notificationsCheck.IsChecked = s.NotificationsEnabled;
        _notificationSoundCheck.IsChecked = s.NotificationSoundEnabled;
        _inAppEphemeralCheck.IsChecked = s.InAppEphemeralStatusEnabled;
        _inAppBackupWarningsCheck.IsChecked = s.InAppBackupWarningsEnabled;
        _minimizeTrayCheck.IsChecked = s.MinimizeToTray;
        _showPlatformColumnCheck.IsChecked = s.ShowPlatformColumn;
        _showBackupSizeColumnCheck.IsChecked = s.ShowBackupSizeColumn;

        var durationSec = Math.Clamp(s.StatusMessageDurationSeconds, 1, 5);
        _statusMessageDurationCombo.SetSelectedTag(durationSec);

        _suppressThemeComboEvents = true;
        try
        {
            var themeKey = ThemeBridge.NormalizeUiThemeKey(s.UiTheme);
            _themeCombo.SetSelectedTag(themeKey);
        }
        finally
        {
            _suppressThemeComboEvents = false;
        }

        var winPreset = WindowSizeHelper.NormalizeMainWindowPreset(s.MainWindowClientPreset);
        _mainWindowSizeCombo.SetSelectedTag(winPreset);

        _lockResolutionCheck.IsChecked = s.MainWindowLockResolution;
        _replayTeachingTipsNextLaunchCheck.IsChecked = s.ReplayTeachingTipsOnNextLaunch;

        var df = (s.DateFormat ?? "iso").Trim().ToLowerInvariant();
        _dateFormatCombo.SetSelectedTag(df);

        var sm = (s.RunOnStartupMode ?? "disabled").Trim().ToLowerInvariant();
        _startupModeCombo.SetSelectedTag(sm);

        SyncAutoBackupDependentUi();
        SyncNotificationDependentUi();
        SyncInAppStatusDependentUi();
        ReloadCompressionFields();
        _compressionBaseline = ReadCompressionBaselineFromUi();
        _baselinePayload = BuildPayloadFromUi();
        }
        finally
        {
            _suppressMainWindowLivePreview = false;
        }
    }

    private MainSettingsPayload BuildPayloadFromUi()
    {
        var dateFmt = _dateFormatCombo.GetSelectedStringTag("iso");
        var startupMode = _startupModeCombo.GetSelectedStringTag("disabled");
        var durationTag = Math.Clamp(_statusMessageDurationCombo.GetSelectedIntTag(3), 1, 5);

        var fresh = _vm.LoadSettingsUi();
        var winPreset = WindowSizeHelper.NormalizeMainWindowPreset(
            _mainWindowSizeCombo.GetSelectedStringTag(WindowSizeHelper.MainWindowPreset800));
        var customW = fresh.MainWindowCustomWidth;
        var customH = fresh.MainWindowCustomHeight;
        if (winPreset == WindowSizeHelper.MainWindowPresetCustom
            && App.MainWindowRef is { } mw
            && WindowSizeHelper.TryGetClientSize(mw, out var cw, out var ch))
        {
            customW = cw;
            customH = ch;
        }

        return new MainSettingsPayload(
            _minimizeTrayCheck.IsChecked == true,
            _showDuplicateTitlesCheck.IsChecked == true,
            _autoBackupCheck.IsChecked == true,
            (int)_frequencyBox.Value,
            (int)_retentionBox.Value,
            _subfolderCheck.IsChecked == true,
            _defaultBackupPathForSave.Trim(),
            string.Empty,
            _notificationsCheck.IsChecked == true,
            _notificationSoundCheck.IsChecked == true,
            _inAppEphemeralCheck.IsChecked == true,
            _inAppBackupWarningsCheck.IsChecked == true,
            _backupEstimateCheck.IsChecked == true,
            _collisionWarnCheck.IsChecked == true,
            dateFmt,
            startupMode,
            ThemeBridge.NormalizeUiThemeKey(_themeCombo.GetSelectedStringTag("dark")),
            durationTag,
            winPreset,
            customW,
            customH,
            _lockResolutionCheck.IsChecked == true,
            _replayTeachingTipsNextLaunchCheck.IsChecked == true,
            _showPlatformColumnCheck.IsChecked == true,
            _showBackupSizeColumnCheck.IsChecked == true);
    }

    /// <returns><see langword="true"/> if main and/or compression settings were written.</returns>
    public async Task<bool> SaveAsync()
    {
        var payload = BuildPayloadFromUi();
        var compNow = ReadCompressionBaselineFromUi();
        var replayDisk = _store.Get(MainViewModel.ReplayTeachingTipsOnNextLaunchSettingKey, false);
        var mainChanged = payload != _baselinePayload || payload.ReplayTeachingTipsOnNextLaunch != replayDisk;
        var compChanged = compNow != _compressionBaseline;
        if (!mainChanged && !compChanged)
        {
            await Task.CompletedTask;
            return false;
        }

        if (mainChanged)
        {
            await _vm.SaveSettingsAsync(payload);
            ThemeBridge.ApplyFromUiThemeKey(payload.UiTheme);
            SyncShellThemeFromMainPage();
            _baselinePayload = payload;
        }

        if (compChanged)
        {
            WriteCompressionSettingsFromUi();
            _compressionBaseline = compNow;
        }

        return true;
    }

    private async void ThemeDropdown_SelectionChanged(object? sender, GsbtSettingsDropdownSelectionChangedEventArgs e)
    {
        if (_suppressThemeComboEvents)
        {
            return;
        }

        if (e.SelectedTag is not string tag)
        {
            return;
        }

        var normalized = ThemeBridge.NormalizeUiThemeKey(tag);
        _store.Set("ui_theme", normalized);
        if (_liveThemeApplyAsync is not null)
        {
            try
            {
                await _liveThemeApplyAsync(normalized);
            }
            catch
            {
                ThemeBridge.ApplyFromUiThemeKey(normalized);
                SyncShellThemeFromMainPage();
            }
        }
        else
        {
            ThemeBridge.ApplyFromUiThemeKey(normalized);
            SyncShellThemeFromMainPage();
        }

        _afterLiveThemeApply?.Invoke();
    }

    private void SyncAutoBackupDependentUi()
    {
        var on = _autoBackupCheck.IsChecked == true;
        _frequencyBox.IsEnabled = on;
        _retentionBox.IsEnabled = on;
    }

    private void SyncNotificationDependentUi()
    {
        var windowsOn = _notificationsCheck.IsChecked == true;
        _notificationSoundCheck.IsEnabled = windowsOn;
    }

    private void SyncInAppStatusDependentUi()
    {
        var on = _inAppEphemeralCheck.IsChecked == true;
        _statusMessageDurationCombo.IsEnabled = on;
    }

    private void AddSettingsSectionTitle(StackPanel root, string title, bool largeTopMargin = true, double bottomMargin = 7)
    {
        var sectionTitle = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = new FontWeight(600),
            Margin = new Thickness(2, largeTopMargin ? 10 : 2, 0, bottomMargin),
        };
        sectionTitle.Foreground = TryBrush("GsbtBodyTextBrush");
        _themedForegroundTextBlocks.Add((sectionTitle, "GsbtBodyTextBrush"));
        root.Children.Add(sectionTitle);
    }


    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private StackPanel BuildBackupTab(Thickness buttonPadding)
    {
        var root = new StackPanel();
        ApplySettingsTabShell(root);
        AddSettingsSectionTitle(root, "Backup", largeTopMargin: false);

        _autoBackupCheck = new CheckBox
        {
            Content = "Automatic backup when save files change",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_autoBackupCheck);
        _autoBackupCheck.Checked += (_, _) => SyncAutoBackupDependentUi();
        _autoBackupCheck.Unchecked += (_, _) => SyncAutoBackupDependentUi();

        _frequencyBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 1440,
            FontSize = CompactFont,
            MinWidth = 168,
            MinHeight = SettingsControlHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        _retentionBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 99,
            FontSize = CompactFont,
            MinWidth = 168,
            MinHeight = SettingsControlHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var autoGroup = new StackPanel { Spacing = CardInnerSpacing };
        autoGroup.Children.Add(_autoBackupCheck);
        var dependent = new StackPanel { Spacing = CardInnerSpacing, Margin = new Thickness(10, 0, 0, 0) };
        dependent.Children.Add(
            CreateSettingRow(
                "Minimum minutes between auto-backups (per game)",
                description: null,
                _frequencyBox));
        dependent.Children.Add(
            CreateSettingRow(
                "Backups to keep (per game)",
                description: null,
                _retentionBox));
        autoGroup.Children.Add(dependent);
        root.Children.Add(WrapInSettingsCard(autoGroup));

        _subfolderCheck = new CheckBox { Content = "Subfolders per game", FontSize = CompactFont };
        ConfigureCheckBox(_subfolderCheck);
        root.Children.Add(WrapInSettingsCard(_subfolderCheck));

        _backupEstimateCheck = new CheckBox
        {
            Content = "Show estimate backup size prompt",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_backupEstimateCheck);
        root.Children.Add(WrapInSettingsCard(_backupEstimateCheck));

        _collisionWarnCheck = new CheckBox
        {
            Content = "Warn when game names share the same backup folder after sanitizing",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_collisionWarnCheck);
        SetDelayedSettingsToolTip(
            _collisionWarnCheck,
            "If two titles sanitize to the same folder name (e.g. colons vs asterisks removed), retention can delete the wrong backups.");
        root.Children.Add(WrapInSettingsCard(_collisionWarnCheck));

        _showDuplicateTitlesCheck = new CheckBox
        {
            Content = "List every detected title even when several share one save folder",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_showDuplicateTitlesCheck);
        root.Children.Add(
            WrapInSettingsCard(
                CreateStackedCard(
                    "Same-folder installs",
                    description: null,
                    _showDuplicateTitlesCheck)));

        _defaultBackupPathDisplay = new TextBlock
        {
            FontSize = CompactFont,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var browseButton = new Button
        {
            Content = "Browse…",
            FontSize = CompactFont,
            MinHeight = SettingsControlHeight,
            MaxHeight = SettingsControlHeight,
            Height = SettingsControlHeight,
            Padding = buttonPadding,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style
        };
        browseButton.Click += BrowseDefaultBackup_Click;
        var pathGrid = new Grid { ColumnSpacing = 8 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_defaultBackupPathDisplay, 0);
        Grid.SetColumn(browseButton, 1);
        _defaultBackupPathDisplay.VerticalAlignment = VerticalAlignment.Center;
        browseButton.VerticalAlignment = VerticalAlignment.Center;
        pathGrid.Children.Add(_defaultBackupPathDisplay);
        pathGrid.Children.Add(browseButton);

        root.Children.Add(
            WrapInSettingsCard(
                CreateStackedCard(
                    "Default backup folder",
                    description: null,
                    pathGrid)));

        return root;
    }

    private StackPanel BuildSystemTab()
    {
        var root = new StackPanel();
        ApplySettingsTabShell(root);
        AddSettingsSectionTitle(root, "System", largeTopMargin: false);

        _themeCombo = CreateSettingsDropdown(SettingsSystemComboMaxWidth);
        _themeCombo.AddOption("Use system settings", "system");
        _themeCombo.AddOption("Light", "light");
        _themeCombo.AddOption("Dark", "dark");
        _themeCombo.SetSelectedTag("dark");
        _themeCombo.SelectionChanged += ThemeDropdown_SelectionChanged;
        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "Application theme",
                    description: null,
                    _themeCombo,
                    intrinsicComboWidth: SettingsSystemComboMaxWidth)));

        _mainWindowSizeCombo = CreateSettingsDropdown(SettingsSystemComboMaxWidth);
        _mainWindowSizeCombo.AddOption("800 × 600 (nominal)", WindowSizeHelper.MainWindowPreset800);
        _mainWindowSizeCombo.AddOption("1024 × 768 (nominal)", WindowSizeHelper.MainWindowPreset1024);
        _mainWindowSizeCombo.AddOption("Custom (last window size)", WindowSizeHelper.MainWindowPresetCustom);
        _mainWindowSizeCombo.SelectionChanged += (_, _) => TryApplyMainWindowLayoutLivePreview();
        _lockResolutionCheck = new CheckBox
        {
            Content = "Lock this resolution",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_lockResolutionCheck);
        _lockResolutionCheck.Checked += (_, _) => TryApplyMainWindowLayoutLivePreview();
        _lockResolutionCheck.Unchecked += (_, _) => TryApplyMainWindowLayoutLivePreview();
        var windowSizeCardInner = new StackPanel { Spacing = CardInnerSpacing };
        windowSizeCardInner.Children.Add(
            CreateSettingRow(
                "Main window size",
                description: null,
                _mainWindowSizeCombo,
                intrinsicComboWidth: SettingsSystemComboMaxWidth));
        windowSizeCardInner.Children.Add(_lockResolutionCheck);
        root.Children.Add(WrapInSettingsCard(windowSizeCardInner));

        _startupModeCombo = CreateSettingsDropdown(SettingsSystemComboMaxWidth);
        _startupModeCombo.AddOption("Don't run on startup", "disabled");
        _startupModeCombo.AddOption("Normal", "normal");
        _startupModeCombo.AddOption("Minimized", "minimized");
        _startupModeCombo.AddOption("Hidden", "hidden");
        _startupModeCombo.SetSelectedTag("disabled");
        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "Run on Windows startup",
                    description: null,
                    _startupModeCombo,
                    intrinsicComboWidth: SettingsSystemComboMaxWidth)));

        _minimizeTrayCheck = new CheckBox
        {
            Content = "Minimize to tray when closing the window",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_minimizeTrayCheck);
        root.Children.Add(WrapInSettingsCard(_minimizeTrayCheck));

        AddSettingsSectionTitle(root, "Game list");

        _showPlatformColumnCheck = new CheckBox
        {
            Content = "Show Platform column",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_showPlatformColumnCheck);

        _showBackupSizeColumnCheck = new CheckBox
        {
            Content = "Show Backup Size column",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_showBackupSizeColumnCheck);

        var gameListInner = new StackPanel { Spacing = CardInnerSpacing };
        gameListInner.Children.Add(_showPlatformColumnCheck);
        gameListInner.Children.Add(_showBackupSizeColumnCheck);
        root.Children.Add(WrapInSettingsCard(gameListInner));

        _dateFormatCombo = CreateSettingsDropdown(SettingsSystemComboMaxWidth);
        _dateFormatCombo.AddOption("ISO (YYYY-MM-DD HH:MM)", "iso");
        _dateFormatCombo.AddOption("US (MM/DD/YYYY HH:MM AM/PM)", "us");
        _dateFormatCombo.AddOption("European (DD/MM/YYYY HH:MM)", "european");
        _dateFormatCombo.AddOption("Asian (YYYY/MM/DD HH:MM)", "asian");
        _dateFormatCombo.SetSelectedTag("iso");
        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "Last backup date format",
                    description: null,
                    _dateFormatCombo,
                    intrinsicComboWidth: SettingsSystemComboMaxWidth)));

        AddSettingsSectionTitle(root, "Notifications");

        _notificationsCheck = new CheckBox
        {
            Content = "Enable system notifications",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_notificationsCheck);
        _notificationsCheck.Checked += (_, _) => SyncNotificationDependentUi();
        _notificationsCheck.Unchecked += (_, _) => SyncNotificationDependentUi();

        _notificationSoundCheck = new CheckBox
        {
            Content = "Play system notification sounds",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_notificationSoundCheck);

        var windowsCard = new StackPanel { Spacing = CardInnerSpacing };
        windowsCard.Children.Add(_notificationsCheck);
        windowsCard.Children.Add(_notificationSoundCheck);
        root.Children.Add(
            WrapInSettingsCard(
                CreateStackedCard(
                    "System notifications",
                    description: null,
                    windowsCard)));

        _inAppEphemeralCheck = new CheckBox
        {
            Content = "Short status messages along the bottom",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_inAppEphemeralCheck);
        _inAppEphemeralCheck.Checked += (_, _) => SyncInAppStatusDependentUi();
        _inAppEphemeralCheck.Unchecked += (_, _) => SyncInAppStatusDependentUi();

        _statusMessageDurationCombo = CreateSettingsDropdown(SettingsSystemStatusDurationComboMaxWidth);
        for (var sec = 1; sec <= 5; sec++)
        {
            _statusMessageDurationCombo.AddOption($"{sec} seconds", sec);
        }
        _statusMessageDurationCombo.SetSelectedTag(3);

        _inAppBackupWarningsCheck = new CheckBox
        {
            Content = "Backup integrity notifications, warnings and column highlights",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_inAppBackupWarningsCheck);

        var inAppInner = new StackPanel { Spacing = CardInnerSpacing };
        inAppInner.Children.Add(_inAppBackupWarningsCheck);
        inAppInner.Children.Add(_inAppEphemeralCheck);
        inAppInner.Children.Add(
            CreateSettingRow(
                "Bottom status line duration",
                description: null,
                _statusMessageDurationCombo,
                intrinsicComboWidth: SettingsSystemStatusDurationComboMaxWidth));
        root.Children.Add(
            WrapInSettingsCard(
                CreateStackedCard(
                    "In-app",
                    description: null,
                    inAppInner)));

        _replayTeachingTipsNextLaunchCheck = new CheckBox
        {
            Content = "Show all teaching tips again on next launch",
            FontSize = CompactFont
        };
        ConfigureCheckBox(_replayTeachingTipsNextLaunchCheck);
        root.Children.Add(
            WrapInSettingsCard(
                CreateStackedCard(
                    "Teaching tips",
                    description: null,
                    _replayTeachingTipsNextLaunchCheck)));

        return root;
    }

    private static GsbtSettingsDropdown CreateSettingsDropdown(double? minWidth = null) =>
        new()
        {
            MinWidth = minWidth ?? 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private static void ApplySettingsTabShell(StackPanel root)
    {
        root.Spacing = 8;
        root.MaxWidth = SettingsTabMaxWidth;
        root.HorizontalAlignment = HorizontalAlignment.Center;
        root.Margin = new Thickness(SettingsHorizontalInset, 4, SettingsHorizontalInset, 14);
    }

    private Border WrapInSettingsCard(FrameworkElement inner)
    {
        var b = new Border
        {
            Width = SettingsCardFixedWidth,
            MinWidth = SettingsCardFixedWidth,
            MaxWidth = SettingsCardFixedWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardCornerRadius),
            Padding = new Thickness(10, 7, 10, 7),
            Child = inner
        };
        b.Background = TryBrush("GsbtCardBgBrush");
        b.BorderBrush = TryBrush("GsbtBorderBrush");
        _settingsCardBorders.Add(b);
        return b;
    }

    /// <summary>Hover tooltips for dense settings; delay avoids instant popups.</summary>
    private static void SetDelayedSettingsToolTip(FrameworkElement element, string tipText)
    {
        var tip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = tipText,
                TextWrapping = TextWrapping.WrapWholeWords,
                MaxWidth = 380,
            },
        };
        ToolTipService.SetToolTip(element, tip);
    }

    private Grid CreateSettingRow(string title, string? description, FrameworkElement control, double alignedInputColumnWidth = 0, double? intrinsicComboWidth = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        if (alignedInputColumnWidth > 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(alignedInputColumnWidth),
                MinWidth = alignedInputColumnWidth,
            });
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var titleTb = new TextBlock
        {
            Text = title,
            FontSize = TitleFont,
            FontWeight = new FontWeight(600),
            TextWrapping = TextWrapping.Wrap,
        };
        titleTb.Foreground = TryBrush("GsbtBodyTextBrush");
        _themedForegroundTextBlocks.Add((titleTb, "GsbtBodyTextBrush"));
        left.Children.Add(titleTb);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descTb = new TextBlock
            {
                Text = description,
                FontSize = CompactFont,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            descTb.Foreground = TryBrush("GsbtSecondaryLabelBrush");
            _themedForegroundTextBlocks.Add((descTb, "GsbtSecondaryLabelBrush"));
            left.Children.Add(descTb);
        }

        NormalizeInputControl(control);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (alignedInputColumnWidth == 0)
        {
            switch (control)
            {
                case GsbtSettingsDropdown dd:
                    var dropdownW = intrinsicComboWidth ?? SettingsIntrinsicValueMaxWidth;
                    dd.MinWidth = dropdownW;
                    dd.HorizontalAlignment = HorizontalAlignment.Stretch;
                    break;
                case ComboBox cb:
                    var comboW = intrinsicComboWidth ?? SettingsIntrinsicValueMaxWidth;
                    cb.MinWidth = comboW;
                    cb.MaxWidth = comboW;
                    cb.HorizontalAlignment = HorizontalAlignment.Stretch;
                    break;
                case NumberBox nb:
                    nb.MaxWidth = SettingsIntrinsicValueMaxWidth;
                    break;
            }
        }

        Grid.SetColumn(left, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(left);
        grid.Children.Add(control);

        return grid;
    }

    private StackPanel CreateStackedCard(string title, string? description, FrameworkElement body) =>
        CreateStackedCard(title, description, body, CardInnerSpacing, true);

    private StackPanel CreateStackedCard(string title, string? description, FrameworkElement body, double innerSpacing, bool normalizeBody)
    {
        var sp = new StackPanel { Spacing = innerSpacing };
        var titleTb = new TextBlock
        {
            Text = title,
            FontSize = TitleFont,
            FontWeight = new FontWeight(600),
        };
        titleTb.Foreground = TryBrush("GsbtBodyTextBrush");
        _themedForegroundTextBlocks.Add((titleTb, "GsbtBodyTextBrush"));
        sp.Children.Add(titleTb);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descTb = new TextBlock
            {
                Text = description,
                FontSize = CompactFont,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            descTb.Foreground = TryBrush("GsbtSecondaryLabelBrush");
            _themedForegroundTextBlocks.Add((descTb, "GsbtSecondaryLabelBrush"));
            sp.Children.Add(descTb);
        }

        if (normalizeBody)
        {
            NormalizeInputControl(body);
        }

        body.HorizontalAlignment = HorizontalAlignment.Stretch;
        sp.Children.Add(body);
        return sp;
    }

    private static void NormalizeInputControl(FrameworkElement control)
    {
        switch (control)
        {
            case GsbtSettingsDropdown:
                // Height owned by GsbtSettingsDropdown (DefaultButtonStyle border exceeds 28px).
                break;
            case ComboBox combo:
                combo.MinHeight = SettingsControlHeight;
                combo.MaxHeight = SettingsControlHeight;
                combo.Height = SettingsControlHeight;
                break;
            case NumberBox number:
                number.MinHeight = SettingsControlHeight;
                number.MaxHeight = SettingsControlHeight;
                number.Height = SettingsControlHeight;
                break;
            case TextBox text:
                text.MinHeight = SettingsControlHeight;
                text.MaxHeight = SettingsControlHeight;
                text.Height = SettingsControlHeight;
                text.Padding = new Thickness(8, 3, 8, 3);
                text.VerticalContentAlignment = VerticalAlignment.Center;
                break;
            case TextBlock block:
                block.MinHeight = SettingsControlHeight;
                break;
        }
    }

    private void ApplyDefaultBackupPathDisplay()
    {
        var path = _defaultBackupPathForSave.Trim();
        if (string.IsNullOrEmpty(path))
        {
            _defaultBackupPathDisplay.Text = "No default backup folder is set. Use Browse… to choose where backups go.";
            _defaultBackupPathDisplay.Foreground = TryBrush("GsbtSecondaryLabelBrush");
        }
        else
        {
            _defaultBackupPathDisplay.Text = path;
            _defaultBackupPathDisplay.Foreground = TryBrush("GsbtBodyTextBrush");
        }
    }

    /// <summary>Resize the main window from the System tab without persisting until OK (Cancel restores on exit).</summary>
    private void TryApplyMainWindowLayoutLivePreview()
    {
        if (_suppressMainWindowLivePreview)
        {
            return;
        }

        if (App.MainWindowRef is not Window w)
        {
            return;
        }

        var tag = _mainWindowSizeCombo.GetSelectedStringTag(WindowSizeHelper.MainWindowPreset800);
        var preset = WindowSizeHelper.NormalizeMainWindowPreset(tag);
        var locked = _lockResolutionCheck.IsChecked == true;
        var fresh = _vm.LoadSettingsUi();
        var cw = fresh.MainWindowCustomWidth;
        var ch = fresh.MainWindowCustomHeight;
        if (preset == WindowSizeHelper.MainWindowPresetCustom
            && WindowSizeHelper.TryGetClientSize(w, out var ww, out var hh))
        {
            cw = ww;
            ch = hh;
        }

        WindowSizeHelper.ApplyMainWindowLayoutPreview(w, preset, cw, ch, locked);
    }

    private void ConfigureCheckBox(CheckBox checkBox)
    {
        if (checkBox.Content is string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                // Nudge text a hair up so it optically centers with WinUI checkbox glyph.
                Margin = new Thickness(6, -1, 0, 0)
            };
            tb.Foreground = TryBrush("GsbtBodyTextBrush");
            _themedForegroundTextBlocks.Add((tb, "GsbtBodyTextBrush"));
            checkBox.Content = tb;
        }

        checkBox.MinHeight = SettingsControlHeight;
        checkBox.VerticalAlignment = VerticalAlignment.Center;
        checkBox.VerticalContentAlignment = VerticalAlignment.Center;
        checkBox.Padding = new Thickness(0);
    }

    private void RefreshAfterUiThemeChange()
    {
        if (_settingsRootGrid is not null)
        {
            _settingsRootGrid.Background = TryBrush("GsbtWindowBgBrush");
        }

        foreach (var b in _settingsCardBorders)
        {
            b.Background = TryBrush("GsbtCardBgBrush");
            b.BorderBrush = TryBrush("GsbtBorderBrush");
        }

        foreach (var (tb, key) in _themedForegroundTextBlocks)
        {
            tb.Foreground = TryBrush(key);
        }

        ApplyDefaultBackupPathDisplay();
        ApplyCompression7zPathDisplay();
    }

    public void CloseSevenZipGetVsWebsiteTeachingTipProgrammatically()
    {
        try
        {
            if (!_sevenZipGetVsWebsiteTeachingTip.IsOpen)
            {
                return;
            }

            _sevenZipGetVsWebsiteTeachingTip.IsOpen = false;
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Uses <see cref="ThemeBridge.IsShellDarkTheme"/> so fills track <see cref="MainPage"/> (not a stale Application theme).</summary>
    private static Brush TryBrush(string key) =>
        ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), key);

    private async void BrowseDefaultBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowRef);
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                _defaultBackupPathForSave = folder.Path;
                ApplyDefaultBackupPathDisplay();
            }
        }
        catch
        {
            // ignore
        }
    }
}
