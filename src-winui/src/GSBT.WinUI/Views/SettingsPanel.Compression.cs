using GSBT.Core.Services;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GSBT.WinUI.Views;

public sealed partial class SettingsPanel
{
    private sealed record CompressionTabBaseline(
        bool AskCompressOnExit,
        int Mx,
        int Threads,
        bool SolidArchive,
        bool ScreenSaverEnabled,
        int ScreenSaverWaitSeconds);

    private StackPanel BuildCompressTab()
    {
        var root = new StackPanel();
        ApplySettingsTabShell(root);
        AddSettingsSectionTitle(root, "Compression", largeTopMargin: false);

        _compressionArchiveModeCombo = CreateSettingsCombo(SettingsIntrinsicValueMaxWidth);
        _compressionArchiveModeCombo.AddOption("Per-file (smooth progress)", "per_file");
        _compressionArchiveModeCombo.AddOption("Solid block (smaller archive)", "solid");
        _compressionArchiveModeCombo.SetSelectedTag("solid");
        SetDelayedSettingsToolTip(
            _compressionArchiveModeCombo,
            "Per-file: steady progress, per-game tracking, quick cancel, larger archives, slower overall. "
            + "Solid block: smaller archives, faster runs, jumpy progress, heavier CPU spikes, slow cancel.");

        root.Children.Add(
            WrapInSettingsCard(
                CreateSettingRow(
                    "Archive mode",
                    description: null,
                    _compressionArchiveModeCombo,
                    intrinsicComboWidth: SettingsIntrinsicValueMaxWidth)));

        var mxIndexMax = SevenZipCompressionLevelMapper.SliderIndexCount - 1;
        _compressionLevelSlider = new Slider
        {
            Minimum = 0,
            Maximum = mxIndexMax,
            StepFrequency = 1,
            TickFrequency = 1,
            TickPlacement = TickPlacement.Outside,
            Value = SevenZipCompressionLevelMapper.SliderIndexFromMx(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _compressionLevelSlider.ValueChanged += (_, _) => UpdateCompressionLevelLabel();
        CompressionLevelSliderUi.WireMxLevelFlyout(_compressionLevelSlider);

        _compressionLevelLabel = new TextBlock
        {
            FontSize = CompactFont,
            MinWidth = 28,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _compressionLevelLabel.Foreground = TryBrush("GsbtBodyTextBrush");
        _themedForegroundTextBlocks.Add((_compressionLevelLabel, "GsbtBodyTextBrush"));

        var levelRow = new Grid { ColumnSpacing = 12 };
        levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_compressionLevelSlider, 0);
        Grid.SetColumn(_compressionLevelLabel, 1);
        levelRow.Children.Add(_compressionLevelSlider);
        levelRow.Children.Add(_compressionLevelLabel);
        SetDelayedSettingsToolTip(
            levelRow,
            "Levels 0, 1, 3, 5, 7, 9 only (real 7-Zip tiers). 0 = store … 9 = maximum. Archives use bundled 7-Zip (.7z LZMA2).");

        _compressionThreadsSliderMax = CompressionOptionsResolver.LogicalProcessorCount;
        _compressionThreadsSlider = new Slider
        {
            Minimum = 0,
            Maximum = _compressionThreadsSliderMax,
            StepFrequency = 1,
            TickFrequency = 1,
            TickPlacement = TickPlacement.Outside,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _compressionThreadsSlider.ValueChanged += (_, _) => UpdateCompressionThreadsLabel();

        _compressionThreadsLabel = new TextBlock
        {
            FontSize = CompactFont,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Auto",
        };
        _compressionThreadsLabel.Foreground = TryBrush("GsbtBodyTextBrush");
        _themedForegroundTextBlocks.Add((_compressionThreadsLabel, "GsbtBodyTextBrush"));

        var threadsRow = new Grid { ColumnSpacing = 12 };
        threadsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        threadsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_compressionThreadsSlider, 0);
        Grid.SetColumn(_compressionThreadsLabel, 1);
        threadsRow.Children.Add(_compressionThreadsSlider);
        threadsRow.Children.Add(_compressionThreadsLabel);
        SetDelayedSettingsToolTip(
            threadsRow,
            $"Auto lets 7-Zip choose thread count (recommended). Slide right to cap cores (1 … {_compressionThreadsSliderMax} on this PC).");

        var slidersInner = new StackPanel { Spacing = CardInnerSpacing };
        slidersInner.Children.Add(
            CreateSettingRow(
                "Compression level",
                description: null,
                levelRow,
                CompressionTabInputColumnWidth));
        slidersInner.Children.Add(
            CreateSettingRow(
                "CPU threads",
                description: null,
                threadsRow,
                CompressionTabInputColumnWidth));
        root.Children.Add(WrapInSettingsCard(slidersInner));

        AddSettingsSectionTitle(root, "Screen saver");
        _screenSaverEnabledCheck = new CheckBox
        {
            Content = "Enable screen saver",
            FontSize = CompactFont,
        };
        ConfigureCheckBox(_screenSaverEnabledCheck);
        _screenSaverEnabledCheck.Checked += (_, _) => SyncScreenSaverDependentUi();
        _screenSaverEnabledCheck.Unchecked += (_, _) => SyncScreenSaverDependentUi();

        _screenSaverWaitCombo = CreateSettingsCombo(SettingsIntrinsicValueMaxWidth);
        for (var sec = 10; sec <= 60; sec += 10)
        {
            _screenSaverWaitCombo.AddOption($"{sec} seconds", sec);
        }

        _screenSaverWaitCombo.SetSelectedTag(60);
        var screenSaverInner = new StackPanel { Spacing = CardInnerSpacing };
        screenSaverInner.Children.Add(_screenSaverEnabledCheck);
        screenSaverInner.Children.Add(
            CreateSettingRow(
                "Wait time",
                description: null,
                _screenSaverWaitCombo,
                intrinsicComboWidth: SettingsIntrinsicValueMaxWidth));
        root.Children.Add(WrapInSettingsCard(screenSaverInner));

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
                "Archives are written in your backup folder as Backups_<date>.7z. Root-level Backups_* files are never included inside the next archive. "
                + "Compression level sets how hard 7-Zip squeezes data; archive mode chooses solid vs per-file packing; CPU threads is best left on Auto. "
                + "When you fully exit (not tray minimize), you can be asked to compress first if Ask to compress backups when closing is enabled.",
            FontSize = CompactFont,
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        foot.Foreground = TryBrush("GsbtSecondaryLabelBrush");
        _themedForegroundTextBlocks.Add((foot, "GsbtSecondaryLabelBrush"));
        root.Children.Add(WrapInSettingsCard(foot));

        UpdateCompressionLevelLabel();
        UpdateCompressionThreadsLabel();

        return root;
    }

    private void UpdateCompressionLevelLabel()
    {
        var index = (int)Math.Round(_compressionLevelSlider.Value);
        _compressionLevelLabel.Text = SevenZipCompressionLevelMapper.MxFromSliderIndex(index).ToString();
    }

    private void UpdateCompressionThreadsLabel()
    {
        var threads = (int)Math.Round(_compressionThreadsSlider.Value);
        _compressionThreadsLabel.Text = threads <= 0 ? "Auto" : threads.ToString();
    }

    private void ReloadCompressionFields()
    {
        _askCompressOnExitCheck.IsChecked = _store.Get("ask_compress_on_exit", false);

        var level = _store.Get("compression_7z_level", -1);
        if (level < 0)
        {
            var legacyPreset = _store.Get("compression_preset", "deflate_balanced") ?? "deflate_balanced";
            level = CompressionOptionsResolver.MapLegacyPresetToLevel(legacyPreset);
        }

        _compressionLevelSlider.Value = SevenZipCompressionLevelMapper.SliderIndexFromMx(
            CompressionOptionsResolver.NormalizeLevel(level));
        UpdateCompressionLevelLabel();

        _compressionThreadsSliderMax = CompressionOptionsResolver.LogicalProcessorCount;
        _compressionThreadsSlider.Maximum = _compressionThreadsSliderMax;
        var threads = CompressionOptionsResolver.NormalizeThreadCount(
            _store.Get("compression_7z_threads", 0),
            _compressionThreadsSliderMax);
        _compressionThreadsSlider.Value = threads;
        UpdateCompressionThreadsLabel();

        var solid = _store.Get(CompressionOptionsResolver.SolidArchiveSettingsKey, true);
        _compressionArchiveModeCombo.SetSelectedTag(solid ? "solid" : "per_file");

        _screenSaverEnabledCheck.IsChecked = ScreenSaverSettings.IsEnabled(_store);
        _screenSaverWaitCombo.SetSelectedTag(ScreenSaverSettings.GetWaitSeconds(_store));
        SyncScreenSaverDependentUi();
    }

    private CompressionTabBaseline ReadCompressionBaselineFromUi()
    {
        var waitSeconds = ScreenSaverSettings.NormalizeWaitSeconds(_screenSaverWaitCombo.GetSelectedIntTag(60));
        var threads = CompressionOptionsResolver.NormalizeThreadCount(
            (int)Math.Round(_compressionThreadsSlider.Value),
            _compressionThreadsSliderMax);
        return new CompressionTabBaseline(
            _askCompressOnExitCheck.IsChecked == true,
            SevenZipCompressionLevelMapper.MxFromSliderIndex((int)Math.Round(_compressionLevelSlider.Value)),
            threads,
            string.Equals(_compressionArchiveModeCombo.GetSelectedStringTag("per_file"), "solid", StringComparison.OrdinalIgnoreCase),
            _screenSaverEnabledCheck.IsChecked == true,
            waitSeconds);
    }

    private void WriteCompressionSettingsFromUi()
    {
        var b = ReadCompressionBaselineFromUi();
        _store.Set("ask_compress_on_exit", b.AskCompressOnExit);
        _store.Set("compression_7z_level", b.Mx);
        _store.Set("compression_7z_threads", b.Threads);
        _store.Set(CompressionOptionsResolver.SolidArchiveSettingsKey, b.SolidArchive);
        _store.Set("compression_preset", CompressionOptionsResolver.PresetNative7z);
        _store.Set(ScreenSaverSettings.EnabledKey, b.ScreenSaverEnabled);
        _store.Set(ScreenSaverSettings.WaitSecondsKey, b.ScreenSaverWaitSeconds);
    }

    private void SyncScreenSaverDependentUi()
    {
        var enabled = _screenSaverEnabledCheck.IsChecked == true;
        _screenSaverWaitCombo.IsEnabled = enabled;
    }
}
