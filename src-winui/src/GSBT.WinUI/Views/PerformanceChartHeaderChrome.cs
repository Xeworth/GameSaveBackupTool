using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Views;

internal static class PerformanceChartHeaderChrome
{
    private const double HeaderIconButtonSize = 22;

    public static Button CreateCheckpointToggleButton(
        PerformanceSparkline chart,
        SettingsStore? settings,
        PerformanceChartCheckpointScope scope)
    {
        chart.ShowCheckpoints = settings is null
            || PerformanceChartDisplaySettings.ShowCheckpoints(scope, settings);

        var btn = new Button
        {
            Width = HeaderIconButtonSize,
            Height = HeaderIconButtonSize,
            MinWidth = HeaderIconButtonSize,
            MinHeight = HeaderIconButtonSize,
            Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(32, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = "\uE81E",
                FontSize = 11,
                Opacity = chart.ShowCheckpoints ? 1 : 0.45,
            },
        };
        ToolTipService.SetToolTip(btn, "Show or hide test start markers");
        btn.Click += (_, _) =>
        {
            chart.ShowCheckpoints = !chart.ShowCheckpoints;
            if (btn.Content is FontIcon icon)
            {
                icon.Opacity = chart.ShowCheckpoints ? 1 : 0.45;
            }

            if (settings is not null)
            {
                var key = scope switch
                {
                    PerformanceChartCheckpointScope.Combined => PerformanceChartDisplaySettings.ShowCheckpointsCombinedKey,
                    PerformanceChartCheckpointScope.Cpu => PerformanceChartDisplaySettings.ShowCheckpointsCpuKey,
                    PerformanceChartCheckpointScope.Ram => PerformanceChartDisplaySettings.ShowCheckpointsRamKey,
                    _ => PerformanceChartDisplaySettings.ShowCheckpointsCombinedKey,
                };
                settings.Set(key, chart.ShowCheckpoints);
            }

            chart.Redraw();
        };
        return btn;
    }

    public static void AddHeaderButtons(
        Grid header,
        Button? expandButton,
        PerformanceSparkline chart,
        SettingsStore? settings,
        PerformanceChartCheckpointScope scope)
    {
        var col = 1;
        if (expandButton is not null)
        {
            Grid.SetColumn(expandButton, col++);
            header.Children.Add(expandButton);
        }

        var checkpointBtn = CreateCheckpointToggleButton(chart, settings, scope);
        Grid.SetColumn(checkpointBtn, col);
        header.Children.Add(checkpointBtn);
    }
}
