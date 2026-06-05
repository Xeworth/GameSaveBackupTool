using GSBT.WinUI;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GSBT.WinUI.Views;

/// <summary>Segoe Fluent / MDL2 footer command icons + progressive icon-only collapse when width is tight.</summary>
public partial class MainPage
{
    private const double FooterHorizontalPaddingTotal = 20;
    private const int FooterCollapseSlotCount = 9;

    /// <summary>Icon glyph codes (PUA). Same code can look different per font — we render with <see cref="ResolveFooterSymbolFontFamily"/> (Fluent on Win11).</summary>
    private static class FooterGlyphs
    {
        public const string Scan = "\uE721";       // Find
        public const string AddCustomGame = "\uE8F4"; // Add
        public const string Filter = "\uE71C";
        public const string Monitor = "\uE756";    // CommandPrompt
        public const string Help = "\uE9CE";
        public const string Settings = "\uE713";   // Setting
        public const string Close = "\uF3B1";      // SignOut
        public const string Backup = "\uE74E";     // Save
        public const string Compress = "\uE792";   // Library
        public const string Cancel = "\uE711";
    }

    private const double FooterIconDefaultSize = 16;

    /// <summary>Shared icon chrome for one footer command (all MDL2 glyphs use the same knobs).</summary>
    /// <param name="Size">Viewbox height and FontIcon size in pixels (typical 12–16). Not a 0–1 scale — use <see cref="Scaled"/> for that.</param>
    /// <param name="StretchX">Widen on X only when &gt; 1 (1 = square).</param>
    /// <param name="Margin">Optional nudge when icon-only (e.g. fix bottom clip).</param>
    private readonly record struct FooterIconStyle(double Size = FooterIconDefaultSize, double StretchX = 1.0, Thickness? Margin = null)
    {
        public static FooterIconStyle Default { get; } = new(FooterIconDefaultSize);

        /// <param name="scale">Multiplier of <see cref="FooterIconDefaultSize"/> (0.9 ≈ 90% of 16px).</param>
        public static FooterIconStyle Scaled(double scale, double stretchX = 1.0, Thickness? margin = null) =>
            new(FooterIconDefaultSize * scale, stretchX, margin);

        public static FooterIconStyle Help { get; } = new(15.5, 1.0);
        public static FooterIconStyle Settings { get; } = new(15.5, 1.0, new Thickness(0, -2, 0, 0));
        /// <summary>SignOut glyph — slightly smaller + nudge up if the arrow clips at the bottom.</summary>
        public static FooterIconStyle Close { get; } = Scaled(1.15, 1, new Thickness(0, 0, 0, 0));
    }

    private readonly FrameworkElement?[] _footerCollapseIcons = new FrameworkElement?[FooterCollapseSlotCount];
    private readonly TextBlock?[] _footerCollapseLabels = new TextBlock?[FooterCollapseSlotCount];
    private TextBlock? _scanCommandBarLabel;

    private bool _footerOverflowApplying;

    private void InitializeMainCommandBarChrome()
    {
        var scanRow = CreateIconLabelRow("Scan for games", FooterGlyphs.Scan, FooterIconStyle.Default, out var scanIcon, out var scanLabel);
        _scanCommandBarLabel = scanLabel;
        ScanPrimaryButton.Content = scanRow;
        _footerCollapseIcons[6] = scanIcon;
        _footerCollapseLabels[6] = scanLabel;

        AddCustomGameButton.Content = CreateIconLabelRow(
            "Add custom game", FooterGlyphs.AddCustomGame, FooterIconStyle.Default, out var addIcon, out var addLabel);
        _footerCollapseIcons[5] = addIcon;
        _footerCollapseLabels[5] = addLabel;

        FilterCommandButton.Content = CreateIconLabelRow(
            ViewModel.FilterButtonText, FooterGlyphs.Filter, FooterIconStyle.Default, out var filterIcon, out var filterLabel);
        filterLabel.SetBinding(
            TextBlock.TextProperty,
            new Binding
            {
                Path = new PropertyPath(nameof(MainViewModel.FilterButtonText)),
                Source = ViewModel,
                Mode = BindingMode.OneWay
            });
        _footerCollapseIcons[4] = filterIcon;
        _footerCollapseLabels[4] = filterLabel;

        MonitorCommandButton.Content = CreateIconLabelRow(
            "Monitor", FooterGlyphs.Monitor, FooterIconStyle.Default, out var monitorIcon, out var monitorLabel);
        _footerCollapseIcons[3] = monitorIcon;
        _footerCollapseLabels[3] = monitorLabel;

        var toolsRow = CreateIconLabelRow(
            "Help", FooterGlyphs.Help, FooterIconStyle.Help, out var toolsIcon, out var toolsLabel);
        FontFamily? toolsChevronFont = null;
        if (Application.Current.Resources.TryGetValue("SymbolThemeFontFamily", out var symFf) && symFf is FontFamily ff)
        {
            toolsChevronFont = ff;
        }

        toolsRow.Children.Add(
            new FontIcon
            {
                Glyph = "\uE70E",
                FontSize = 11,
                FontFamily = toolsChevronFont,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
        ToolsMenuButton.Content = toolsRow;
        _footerCollapseIcons[2] = toolsIcon;
        _footerCollapseLabels[2] = toolsLabel;

        SettingsPageButton.Content = CreateIconLabelRow(
            "Settings", FooterGlyphs.Settings, FooterIconStyle.Settings, out var settingsIcon, out var settingsLabel);
        _footerCollapseIcons[1] = settingsIcon;
        _footerCollapseLabels[1] = settingsLabel;

        QuitButton.Content = CreateIconLabelRow(
            "Close", FooterGlyphs.Close, FooterIconStyle.Close, out var quitIcon, out var quitLabel);
        _footerCollapseIcons[0] = quitIcon;
        _footerCollapseLabels[0] = quitLabel;

        RefreshBackupCompressFooterChrome();

        ApplyFooterCollapseLevel(0);

        ApplyFooterMenuFlyoutsOpenUpward();
    }

    /// <summary>Morph Backup/Compress between primary actions and Cancel while a long operation runs.</summary>
    private void RefreshBackupCompressFooterChrome()
    {
        if (ViewModel.FooterBackupShowsCancel)
        {
            BackupButton.Style = (Style)Resources["GsbtToolbarDangerCommandBarButtonStyle"];
            BackupButton.Content = CreateIconLabelRow(
                "Cancel", FooterGlyphs.Cancel, FooterIconStyle.Default, out var backupIcon, out var backupLabel);
            _footerCollapseIcons[8] = backupIcon;
            _footerCollapseLabels[8] = backupLabel;
        }
        else
        {
            BackupButton.Style = (Style)Resources["GsbtAccentCommandBarButtonStyle"];
            BackupButton.Content = CreateIconLabelRow(
                "Backup", FooterGlyphs.Backup, FooterIconStyle.Default, out var backupIcon, out var backupLabel);
            _footerCollapseIcons[8] = backupIcon;
            _footerCollapseLabels[8] = backupLabel;
        }

        if (ViewModel.FooterCompressShowsCancel)
        {
            CompressButton.Style = (Style)Resources["GsbtToolbarDangerCommandBarButtonStyle"];
            CompressButton.Content = CreateIconLabelRow(
                "Cancel", FooterGlyphs.Cancel, FooterIconStyle.Default, out var compressIcon, out var compressLabel);
            _footerCollapseIcons[7] = compressIcon;
            _footerCollapseLabels[7] = compressLabel;
        }
        else
        {
            CompressButton.Style = (Style)Resources["GsbtCommandBarButtonStyle"];
            CompressButton.Content = CreateIconLabelRow(
                "Compress", FooterGlyphs.Compress, FooterIconStyle.Default, out var compressIcon, out var compressLabel);
            _footerCollapseIcons[7] = compressIcon;
            _footerCollapseLabels[7] = compressLabel;
        }

        RequestFooterCommandBarOverflowRelayout();
    }

    /// <summary>Footer menus sit on the bottom edge; pin upward placement on named flyouts.</summary>
    private void ApplyFooterMenuFlyoutsOpenUpward()
    {
        PinFooterFlyoutPlacement(ScanMenuFlyout, FlyoutPlacementMode.TopEdgeAlignedLeft);
        PinFooterFlyoutPlacement(ToolsMenuFlyout, FlyoutPlacementMode.TopEdgeAlignedLeft);
    }

    private void PinFooterFlyoutPlacement(MenuFlyout flyout, FlyoutPlacementMode placement)
    {
        flyout.Placement = placement;
        flyout.ShouldConstrainToRootBounds = false;

        void Pin(object? s, object e)
        {
            flyout.Placement = placement;
            flyout.ShouldConstrainToRootBounds = false;
        }

        flyout.Opening -= Pin;
        flyout.Opening += Pin;
    }

    private static StackPanel CreateIconLabelRow(
        string text,
        string glyph,
        FooterIconStyle icon,
        out FrameworkElement collapseChrome,
        out TextBlock label)
    {
        var boxSize = icon.Size;
        var stretchX = Math.Max(1.0, icon.StretchX);
        collapseChrome = new Viewbox
        {
            Width = boxSize * stretchX,
            Height = boxSize,
            Stretch = stretchX > 1.0 ? Stretch.Fill : Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontFamily = ResolveFooterSymbolFontFamily(),
                FontSize = boxSize,
            },
        };

        collapseChrome.Visibility = Visibility.Collapsed;
        collapseChrome.VerticalAlignment = VerticalAlignment.Center;
        collapseChrome.Margin = icon.Margin ?? new Thickness(0, -1, 0, 0);

        label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { collapseChrome, label } };
    }

    /// <summary>WinUI gallery / SymbolIcon default — Segoe Fluent Icons on Win11, MDL2 fallback on older Windows.</summary>
    private static FontFamily ResolveFooterSymbolFontFamily()
    {
        if (Application.Current.Resources.TryGetValue("SymbolThemeFontFamily", out var ff) && ff is FontFamily family)
        {
            return family;
        }

        return new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    }

    private void FooterCommandRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_footerOverflowApplying || e.NewSize.Width <= 0)
        {
            return;
        }

        try
        {
            _footerOverflowApplying = true;
            UpdateFooterCommandBarOverflow(e.NewSize.Width);
        }
        finally
        {
            _footerOverflowApplying = false;
        }
    }

    private void RequestFooterCommandBarOverflowRelayout()
    {
        if (FooterCommandRow.ActualWidth > 0)
        {
            UpdateFooterCommandBarOverflow(FooterCommandRow.ActualWidth);
        }
    }

    private void UpdateFooterCommandBarOverflow(double rowWidth)
    {
        var budget = rowWidth - FooterHorizontalPaddingTotal;
        if (budget <= 0)
        {
            return;
        }

        var chosen = FooterCollapseSlotCount;
        for (var k = 0; k <= FooterCollapseSlotCount; k++)
        {
            ApplyFooterCollapseLevel(k);
            FooterCommandRow.UpdateLayout();
            LeftCommandBarStack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            RightCommandBarStack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var need = LeftCommandBarStack.DesiredSize.Width + FooterCommandRow.ColumnSpacing + RightCommandBarStack.DesiredSize.Width;
            if (need <= budget)
            {
                chosen = k;
                break;
            }
        }

        ApplyFooterCollapseLevel(chosen);
    }

    /// <summary>When <paramref name="collapseCount"/> is k, the k narrowest-priority commands use icon-only (0 = all show text, icons hidden).</summary>
    private void ApplyFooterCollapseLevel(int collapseCount)
    {
        collapseCount = Math.Clamp(collapseCount, 0, FooterCollapseSlotCount);
        for (var i = 0; i < FooterCollapseSlotCount; i++)
        {
            var chrome = _footerCollapseIcons[i];
            var label = _footerCollapseLabels[i];
            if (chrome is null || label is null)
            {
                continue;
            }

            var iconOnly = i < collapseCount;
            chrome.Visibility = iconOnly ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Simulation child: disable manifest GitHub refresh from the Scan split menu.</summary>
    private void ConfigureScanMenuForSimulation()
    {
        if (!App.IsSandboxSimulationChild)
        {
            return;
        }

        foreach (var item in ScanMenuFlyout.Items)
        {
            if (item is MenuFlyoutItem mi)
            {
                mi.IsEnabled = false;
                ToolTipService.SetToolTip(
                    mi,
                    "Not available in the simulation — avoids downloading the manifest from GitHub. Use the full app to refresh.");
                break;
            }
        }
    }
}
