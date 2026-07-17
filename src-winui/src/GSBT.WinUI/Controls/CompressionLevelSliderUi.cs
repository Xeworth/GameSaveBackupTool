using GSBT.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GSBT.WinUI.Controls;

/// <summary>
/// WinUI slider thumb flyouts bind to raw <see cref="RangeBase.Value"/> (0…5 index).
/// Remap via <see cref="Slider.ThumbToolTipValueConverter"/> to real 7-Zip mx tiers.
/// </summary>
internal static class CompressionLevelSliderUi
{
    private static readonly MxLevelThumbToolTipConverter Converter = new();

    public static void WireMxLevelFlyout(Slider slider) =>
        slider.ThumbToolTipValueConverter = Converter;

    public static Canvas CreateMxTickLabelCanvas(Slider slider, Brush foreground)
    {
        var labels = SevenZipCompressionLevelMapper.SupportedMxLevels
            .Select(level => new TextBlock
            {
                Text = level.ToString(),
                FontSize = 10,
                Foreground = foreground,
                IsHitTestVisible = false,
            })
            .ToArray();

        var canvas = new Canvas
        {
            Height = 14,
            MinHeight = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, -6, 0, 0),
            IsHitTestVisible = false,
        };

        foreach (var label in labels)
        {
            canvas.Children.Add(label);
        }

        void ArrangeLabels()
        {
            var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : slider.ActualWidth;
            if (width <= 1 || labels.Length == 0)
            {
                return;
            }

            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var labelWidth = label.ActualWidth > 0 ? label.ActualWidth : label.DesiredSize.Width;
                var x = labels.Length == 1 ? 0 : width * i / (labels.Length - 1);
                Canvas.SetLeft(label, Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, width - labelWidth)));
                Canvas.SetTop(label, 0);
            }
        }

        canvas.SizeChanged += (_, _) => ArrangeLabels();
        slider.SizeChanged += (_, _) => ArrangeLabels();
        canvas.Loaded += (_, _) => ArrangeLabels();
        slider.Loaded += (_, _) => ArrangeLabels();
        return canvas;
    }

    public static void SetTickLabelForeground(Canvas canvas, Brush foreground)
    {
        foreach (var label in canvas.Children.OfType<TextBlock>())
        {
            label.Foreground = foreground;
        }
    }

    private sealed class MxLevelThumbToolTipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                return SevenZipCompressionLevelMapper.MxFromSliderIndex((int)Math.Round(d)).ToString();
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
