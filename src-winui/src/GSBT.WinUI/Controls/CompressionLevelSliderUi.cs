using GSBT.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

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
