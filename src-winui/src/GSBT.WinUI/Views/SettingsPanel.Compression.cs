using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GSBT.Core.Services;
using GSBT.WinUI;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace GSBT.WinUI.Views;

public sealed partial class SettingsPanel
{
    private sealed record CompressionTabBaseline(bool AskCompressOnExit, string Preset, string SevenFormat, int Mx, int Threads, string SevenPath);

    private StackPanel BuildCompressTab()
    {
        var root = new StackPanel();
        ApplySettingsTabShell(root);
        AddSettingsSectionTitle(root, "Compression engine", largeTopMargin: false);

        _compressionPresetCombo = CreateSettingsDropdown();
        _compressionPresetCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _compressionPresetCombo.AddOption("Store (no compression, fastest)", CompressionOptionsResolver.PresetStore);
        _compressionPresetCombo.AddOption("ZIP — fast deflate", CompressionOptionsResolver.PresetDeflateFast);
        _compressionPresetCombo.AddOption("ZIP — balanced deflate", CompressionOptionsResolver.PresetDeflateBalanced);
        _compressionPresetCombo.AddOption("ZIP — max deflate", CompressionOptionsResolver.PresetDeflateMax);
        _compressionPresetCombo.AddOption("7-Zip engine", CompressionOptionsResolver.PresetSevenZip);
        _compressionPresetCombo.SetSelectedTag(CompressionOptionsResolver.PresetDeflateBalanced);
        _compressionPresetCombo.SelectionChanged += (_, _) => SyncCompressionSubUi();
        SetDelayedSettingsToolTip(
            _compressionPresetCombo,
            "Built-in ZIP is single-threaded. The 7-Zip engine uses 7z.exe for .7z (LZMA2) or .zip.");
        _get7zipButton = new Button
        {
            Content = "Get 7-Zip",
            FontSize = CompactFont,
            MinHeight = UiMetrics.SettingsDropdownMinHeight,
            Padding = new Thickness(12, 4, 12, 4),
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
        };
        _get7zipButton.Click += Get7Zip_Click;
        SetDelayedSettingsToolTip(_get7zipButton, "Download and install the pinned 7-Zip build silently (consent dialog first).");

        _sevenZipOfficialSiteButton = new Button
        {
            Content = "Official 7-Zip site",
            FontSize = CompactFont,
            MinHeight = UiMetrics.SettingsDropdownMinHeight,
            Padding = new Thickness(12, 4, 12, 4),
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
        };
        _sevenZipOfficialSiteButton.Click += SevenZipOfficialSiteButton_Click;
        SetDelayedSettingsToolTip(_sevenZipOfficialSiteButton, "Open 7-zip.org in your browser (latest builds from the vendor).");

        _sevenZipInfoButton = new Button
        {
            MinWidth = 30,
            MinHeight = UiMetrics.SettingsDropdownMinHeight,
            Padding = new Thickness(4, 0, 4, 0),
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\uE946",
                FontSize = 14,
            },
        };
        _sevenZipInfoButton.Click += SevenZipInfoButton_Click;
        SetDelayedSettingsToolTip(_sevenZipInfoButton, "Show a tip below this row: Get 7-Zip vs Official 7-Zip site (pinned version vs latest on the web).");

        _sevenZipEngineActionsStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sevenZipEngineActionsStrip.Children.Add(_get7zipButton);
        _sevenZipEngineActionsStrip.Children.Add(_sevenZipOfficialSiteButton);
        _sevenZipEngineActionsStrip.Children.Add(_sevenZipInfoButton);

        _compressionEngineRowGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = UiMetrics.SettingsDropdownMinHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _compressionEngineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _compressionEngineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_compressionPresetCombo, 0);
        Grid.SetColumn(_sevenZipEngineActionsStrip, 1);
        _compressionEngineRowGrid.Children.Add(_compressionPresetCombo);
        _compressionEngineRowGrid.Children.Add(_sevenZipEngineActionsStrip);

        _sevenZipInstallStatusText = new TextBlock
        {
            FontSize = CompactFont,
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        _sevenZipInstallStatusText.Foreground = TryBrush("GsbtSecondaryLabelBrush");
        _themedForegroundTextBlocks.Add((_sevenZipInstallStatusText, "GsbtSecondaryLabelBrush"));

        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "Preset",
                    description: null,
                    _compressionEngineRowGrid,
                    CompressionTabInputColumnWidth)));

        AddSettingsSectionTitle(root, "Engine settings", largeTopMargin: true);

        _sevenZipGetVsWebsiteTeachingTipBody = new TextBlock
        {
            MaxWidth = 340,
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = BuildSevenZipPinnedVsLatestTeachingTipText(),
        };
        _sevenZipGetVsWebsiteTeachingTip = new TeachingTip
        {
            Title = "Get 7-Zip vs website",
            PreferredPlacement = TeachingTipPlacementMode.Bottom,
            PlacementMargin = new Thickness(0, 0, 0, 10),
            IsLightDismissEnabled = true,
            Content = _sevenZipGetVsWebsiteTeachingTipBody,
            Target = _compressionEngineRowGrid,
        };

        _compression7zFormatCombo = CreateSettingsDropdown(SettingsIntrinsicValueMaxWidth);
        _compression7zFormatCombo.AddOption(".7z archive (LZMA2, multithreaded, recommended)", "7z");
        _compression7zFormatCombo.AddOption(".zip archive (Deflate via 7-Zip)", "zip");
        _compression7zFormatCombo.SetSelectedTag("7z");
        _compression7zFormatCombo.SelectionChanged += (_, _) => SyncCompressionSubUi();
        SetDelayedSettingsToolTip(_compression7zFormatCombo, "Used only when the 7-Zip engine preset is selected.");

        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "7-Zip output format",
                    description: null,
                    _compression7zFormatCombo)));

        _compressionMxBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 9,
            Value = 5,
            FontSize = CompactFont,
            MinWidth = 72,
            MinHeight = SettingsControlHeight,
            MaxHeight = SettingsControlHeight,
            Height = SettingsControlHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        SetDelayedSettingsToolTip(_compressionMxBox, "0 = copy/store … 9 = smallest/slowest. For .7z LZMA2, 5–7 is a good balance.");
        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "7-Zip level (-mx)",
                    description: null,
                    _compressionMxBox)));

        _compressionThreadsBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 128,
            Value = 0,
            FontSize = CompactFont,
            MinWidth = 80,
            MinHeight = SettingsControlHeight,
            MaxHeight = SettingsControlHeight,
            Height = SettingsControlHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        SetDelayedSettingsToolTip(_compressionThreadsBox, "0 = Auto (7-Zip uses logical cores). Set e.g. 16 to cap load.");
        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "7-Zip threads (-mmt)",
                    description: null,
                    _compressionThreadsBox)));

        _compression7zPathDisplay = new TextBlock
        {
            FontSize = CompactFont,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSelectionEnabled = true,
        };

        _browse7zButton = new Button
        {
            Content = "Browse…",
            FontSize = CompactFont,
            MinHeight = SettingsControlHeight,
            MaxHeight = SettingsControlHeight,
            Height = SettingsControlHeight,
            Padding = new Thickness(12, 4, 12, 4),
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
        };
        _browse7zButton.Click += Browse7zExe_Click;
        SetDelayedSettingsToolTip(_browse7zButton, "Pick 7z.exe manually.");

        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_compression7zPathDisplay, 0);
        Grid.SetColumn(_browse7zButton, 1);
        pathRow.Children.Add(_compression7zPathDisplay);
        pathRow.Children.Add(_browse7zButton);
        SetDelayedSettingsToolTip(
            pathRow,
            "Optional override. Empty = search PATH and Program Files\\7-Zip. Use Browse… to set 7z.exe.");

        _sevenZipExecutableCard = WrapInSettingsCard(
            CreateStackedCard(
                "7-Zip executable",
                description: null,
                pathRow));
        root.Children.Add(_sevenZipExecutableCard);

        ApplyCompression7zPathDisplay();

        _sevenZipOnPcCard = WrapInSettingsCard(
            CreateStackedCard(
                "7-Zip on this PC",
                description: App.IsSandboxSimulationChild
                    ? "Sandbox can pretend 7-Zip is installed or missing (Compression UI only)."
                    : null,
                _sevenZipInstallStatusText,
                innerSpacing: 4,
                normalizeBody: false));
        root.Children.Add(_sevenZipOnPcCard);

        AddSettingsSectionTitle(root, "Compress before exit");
        _askCompressOnExitCheck = new CheckBox
        {
            Content = "Ask to compress backups when closing",
            FontSize = CompactFont,
        };
        ConfigureCheckBox(_askCompressOnExitCheck);
        root.Children.Add(WrapInSettingsCard(_askCompressOnExitCheck));

        var foot = new TextBlock
        {
            Text =
                "Archives are written in your backup folder as Backups_<date>.zip or .7z. Root-level Backups_* files are never included inside the next archive. "
                + "When you fully exit (not tray minimize), you can be asked to compress first if Ask to compress backups when closing is enabled.",
            FontSize = CompactFont,
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        foot.Foreground = TryBrush("GsbtSecondaryLabelBrush");
        _themedForegroundTextBlocks.Add((foot, "GsbtSecondaryLabelBrush"));
        root.Children.Add(WrapInSettingsCard(foot));

        return root;
    }

    private void ApplyCompression7zPathDisplay()
    {
        var path = _compression7zPathValue.Trim();
        if (string.IsNullOrEmpty(path))
        {
            _compression7zPathDisplay.Text =
                "Leave empty to auto-detect 7z.exe; use Browse… to point at 7z.exe.";
            _compression7zPathDisplay.Foreground = TryBrush("GsbtSecondaryLabelBrush");
        }
        else
        {
            _compression7zPathDisplay.Text = path;
            _compression7zPathDisplay.Foreground = TryBrush("GsbtBodyTextBrush");
        }
    }

    private void ReloadCompressionFields()
    {
        _askCompressOnExitCheck.IsChecked = _store.Get("ask_compress_on_exit", false);
        var preset = CompressionOptionsResolver.NormalizePreset(_store.Get("compression_preset", CompressionOptionsResolver.PresetDeflateBalanced));
        _compressionPresetCombo.SetSelectedTag(preset);

        var zf = CompressionOptionsResolver.Normalize7zFormat(_store.Get("compression_7z_format", "7z"));
        _compression7zFormatCombo.SetSelectedTag(zf);

        _compressionMxBox.Value = Math.Clamp(_store.Get("compression_7z_level", 5), 0, 9);
        _compressionThreadsBox.Value = Math.Clamp(_store.Get("compression_7z_threads", 0), 0, 128);
        _compression7zPathValue = _store.Get("compression_7z_path", string.Empty) ?? string.Empty;
        ApplyCompression7zPathDisplay();
        SyncCompressionSubUi();
    }

    private CompressionTabBaseline ReadCompressionBaselineFromUi()
    {
        var preset = _compressionPresetCombo.GetSelectedStringTag(CompressionOptionsResolver.PresetDeflateBalanced);
        var fmt = _compression7zFormatCombo.GetSelectedStringTag("7z");
        return new CompressionTabBaseline(
            _askCompressOnExitCheck.IsChecked == true,
            CompressionOptionsResolver.NormalizePreset(preset),
            CompressionOptionsResolver.Normalize7zFormat(fmt),
            (int)Math.Clamp(_compressionMxBox.Value, 0, 9),
            (int)Math.Clamp(_compressionThreadsBox.Value, 0, 128),
            _compression7zPathValue.Trim());
    }

    private void WriteCompressionSettingsFromUi()
    {
        var b = ReadCompressionBaselineFromUi();
        _store.Set("ask_compress_on_exit", b.AskCompressOnExit);
        _store.Set("compression_preset", b.Preset);
        _store.Set("compression_7z_format", b.SevenFormat);
        _store.Set("compression_7z_level", b.Mx);
        _store.Set("compression_7z_threads", b.Threads);
        _store.Set("compression_7z_path", b.SevenPath);
    }

    private void SyncCompressionSubUi()
    {
        var is7 = _compressionPresetCombo.GetSelectedStringTag(string.Empty) == CompressionOptionsResolver.PresetSevenZip;
        var hintInstalled = _vm.HasSevenZipForSettingsHint();
        var showMissing7ZipActions = is7 && !hintInstalled;
        Grid.SetColumnSpan(_compressionPresetCombo, showMissing7ZipActions ? 1 : 2);
        _compressionPresetCombo.Margin = showMissing7ZipActions ? new Thickness(0, 0, 10, 0) : new Thickness(0);
        if (!is7)
        {
            CloseSevenZipGetVsWebsiteTeachingTipProgrammatically();
        }

        _compression7zFormatCombo.IsEnabled = is7;
        _compressionMxBox.IsEnabled = is7;
        _compressionThreadsBox.IsEnabled = is7;
        _browse7zButton.IsEnabled = is7;
        _compression7zPathDisplay.Opacity = is7 ? 1 : 0.55;
        _get7zipButton.Visibility = showMissing7ZipActions ? Visibility.Visible : Visibility.Collapsed;
        _sevenZipOfficialSiteButton.Visibility = showMissing7ZipActions ? Visibility.Visible : Visibility.Collapsed;
        _sevenZipInfoButton.Visibility = showMissing7ZipActions ? Visibility.Visible : Visibility.Collapsed;
        _sevenZipEngineActionsStrip.Visibility = showMissing7ZipActions ? Visibility.Visible : Visibility.Collapsed;
        var allowNetInstall = is7 && !App.IsSandboxSimulationChild;
        _get7zipButton.IsEnabled = allowNetInstall;
        _sevenZipOfficialSiteButton.IsEnabled = showMissing7ZipActions;
        _sevenZipInfoButton.IsEnabled = showMissing7ZipActions;
        ToolTipService.SetToolTip(
            _get7zipButton,
            App.IsSandboxSimulationChild && is7 && !hintInstalled
                ? "Disabled in the simulation — no 7-Zip installer download. Use the full app or install from 7-zip.org."
                : null);
        _sevenZipInstallStatusText.Text = _vm.GetSevenZipInstallStatusUiText();
        _sevenZipOnPcCard.Visibility = is7 ? Visibility.Visible : Visibility.Collapsed;
        _sevenZipExecutableCard.Visibility = is7 && !hintInstalled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildSevenZipPinnedVsLatestTeachingTipText() =>
        $"Get 7-Zip installs this app’s pinned build: 7-Zip {SevenZipDownloadInstall.PinnedDisplayVersion} ({SevenZipDownloadInstall.PinnedBuild}). "
        + "Official 7-Zip site opens 7-zip.org, where you can download the newest release — often newer than this pinned installer.";

    private void SevenZipInfoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _sevenZipGetVsWebsiteTeachingTipBody.Text = BuildSevenZipPinnedVsLatestTeachingTipText();
            _sevenZipGetVsWebsiteTeachingTip.Target = _compressionEngineRowGrid;
            _sevenZipGetVsWebsiteTeachingTip.IsOpen = !_sevenZipGetVsWebsiteTeachingTip.IsOpen;
        }
        catch
        {
            // ignore
        }
    }

    private async void SevenZipOfficialSiteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = await Launcher.LaunchUriAsync(new Uri("https://www.7-zip.org/"));
        }
        catch
        {
            // ignore
        }
    }

    private async void Browse7zExe_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || App.MainWindowRef is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowRef));
        var f = await picker.PickSingleFileAsync();
        if (f is not null)
        {
            _compression7zPathValue = f.Path;
            ApplyCompression7zPathDisplay();
        }
    }

    private async void Get7Zip_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        if (App.IsSandboxSimulationChild)
        {
            var blocked = new ContentDialog
            {
                Title = "Get 7-Zip",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWholeWords,
                    IsTextSelectionEnabled = true,
                    Text =
                        "Installer download is disabled in the simulation window so traffic stays on your real app. "
                        + "Use the full Game Save Backup Tool to run Get 7-Zip, or install 7-Zip from 7-zip.org. "
                        + "You can still pick the 7-Zip engine preset here and use Sandbox monitor → Simulated states to pretend 7-Zip is installed or not.",
                },
            };
            blocked.XamlRoot = XamlRoot;
            blocked.RequestedTheme = ActualTheme;
            await GsbtContentDialog.ShowAsync(blocked);
            return;
        }

        var consent = new ContentDialog
        {
            Title = "Get 7-Zip",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 380,
                Content = new TextBlock
                {
                    Text = SevenZipDownloadInstall.ConsentSummaryText(),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    IsTextSelectionEnabled = true,
                },
            },
        };
        consent.XamlRoot = XamlRoot;
        consent.RequestedTheme = ActualTheme;
        if (await GsbtContentDialog.ShowAsync(consent) != ContentDialogResult.Primary)
        {
            return;
        }

        _vm.StatusText = "7-Zip: downloading / installing… (see Sandbox monitor → Live log → 7zip)";
        var msg = await _vm.InstallSevenZipFromOfficialSiteAsync(
            new Progress<(int percent, string? text)>(v => _vm.StatusText = v.text ?? $"… {v.percent}%"),
            CancellationToken.None);
        _vm.StatusText = msg;
        ReloadCompressionFields();
        _compressionBaseline = ReadCompressionBaselineFromUi();

        var done = new ContentDialog
        {
            Title = "Get 7-Zip",
            CloseButtonText = "Close",
            Content = new TextBlock { Text = msg, TextWrapping = TextWrapping.WrapWholeWords, IsTextSelectionEnabled = true },
        };
        done.XamlRoot = XamlRoot;
        done.RequestedTheme = ActualTheme;
        await GsbtContentDialog.ShowAsync(done);
    }

}
