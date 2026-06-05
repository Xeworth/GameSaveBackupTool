using GSBT.WinUI.Common;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Views;

internal static class BenchmarkResultCardBuilder
{
    private const double CornerActionSize = 22;

    public static Border BuildCompactCard(SandboxCompressionBenchmarkEntry entry, bool darkChrome)
    {
        var titleBrush = ThemeBridge.GetGsbtBrush(
            darkChrome,
            entry.Success ? "GsbtBenchmarkSuccessTitleBrush" : "GsbtBenchmarkFailureTitleBrush");

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = entry.TitleLine,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = titleBrush,
            Margin = new Thickness(0, 0, 4, 0),
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(entry.ArchivePath))
        {
            var openBtn = new Button
            {
                Width = CornerActionSize,
                Height = CornerActionSize,
                MinWidth = CornerActionSize,
                MinHeight = CornerActionSize,
                Padding = new Thickness(0),
                Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtTableGridLineBrush"),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new FontIcon { Glyph = "\uE838", FontSize = 11 },
            };
            ToolTipService.SetToolTip(openBtn, "Show archive in File Explorer");
            var path = entry.ArchivePath;
            openBtn.Click += (_, _) => ExplorerRevealHelper.TryRevealFile(path);
            Grid.SetColumn(openBtn, 1);
            header.Children.Add(openBtn);
        }

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);

        var detail = entry.PriorityDetailText ?? entry.DetailText;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            body.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 10,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
            });
        }

        return new Border
        {
            Width = 445,
            MaxWidth = 445,
            Padding = new Thickness(12, 10, 12, 10),
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = body,
        };
    }

    public static void RefreshTheme(Border card, SandboxCompressionBenchmarkEntry entry, bool darkChrome)
    {
        card.Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush");
        card.BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush");

        if (card.Child is not StackPanel body)
        {
            return;
        }

        foreach (var child in body.Children)
        {
            if (child is TextBlock tb)
            {
                tb.Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");
            }
            else if (child is Grid grid)
            {
                foreach (var gridChild in grid.Children)
                {
                    if (gridChild is TextBlock titleTb)
                    {
                        titleTb.Foreground = ThemeBridge.GetGsbtBrush(
                            darkChrome,
                            entry.Success ? "GsbtBenchmarkSuccessTitleBrush" : "GsbtBenchmarkFailureTitleBrush");
                    }
                }
            }
        }
    }
}
