using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Controls;

/// <summary>Retro VCR-style label: light fill with a thick dark outline.</summary>
public sealed class VcrOutlineTextBlock : UserControl
{
    private const double NativePixelFontSize = 21.0;

    private static readonly (int X, int Y)[] OutlineOffsets =
    [
        (-2, 0), (2, 0), (0, -2), (0, 2),
        (-2, -2), (2, -2), (-2, 2), (2, 2),
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (1, -1), (-1, 1), (1, 1),
    ];

    private readonly Grid _root;
    private readonly TextBlock[] _outlineLayers;
    private readonly TextBlock _foreground;
    private readonly ScaleTransform _scaleTransform;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(VcrOutlineTextBlock),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is VcrOutlineTextBlock block && e.NewValue is string s)
                {
                    block.ApplyText(s);
                }
            }));

    public static readonly DependencyProperty ClockFontSizeProperty =
        DependencyProperty.Register(
            nameof(ClockFontSize),
            typeof(double),
            typeof(VcrOutlineTextBlock),
            new PropertyMetadata(NativePixelFontSize, (d, e) =>
            {
                if (d is VcrOutlineTextBlock block && e.NewValue is double size)
                {
                    block.ApplyVisualScale(size);
                }
            }));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double ClockFontSize
    {
        get => (double)GetValue(ClockFontSizeProperty);
        set => SetValue(ClockFontSizeProperty, value);
    }

    public VcrOutlineTextBlock()
    {
        UseLayoutRounding = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;

        _scaleTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        _root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _scaleTransform,
            RenderTransformOrigin = new Windows.Foundation.Point(0, 0),
        };

        _outlineLayers = new TextBlock[OutlineOffsets.Length];
        for (var i = 0; i < OutlineOffsets.Length; i++)
        {
            var (x, y) = OutlineOffsets[i];
            var layer = CreateLayer("#FF000000", x, y);
            _outlineLayers[i] = layer;
            _root.Children.Add(layer);
        }

        _foreground = CreateLayer("#FFF2F5F7", 0, 0);
        _root.Children.Add(_foreground);
        Content = _root;

        ApplyVisualScale(NativePixelFontSize);
        Loaded += (_, _) => SyncRootAlignment();
        RegisterPropertyChangedCallback(HorizontalAlignmentProperty, (_, _) => SyncRootAlignment());
    }

    private void SyncRootAlignment()
    {
        _root.HorizontalAlignment = HorizontalAlignment;
    }

    private TextBlock CreateLayer(string hex, double offsetX, double offsetY)
    {
        return new TextBlock
        {
            FontFamily = ResolveVcrFontFamily(),
            FontSize = NativePixelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            CharacterSpacing = 80,
            IsTextSelectionEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform { X = offsetX, Y = offsetY },
            Foreground = new SolidColorBrush(ParseColor(hex)),
        };
    }

    private void ApplyText(string value)
    {
        foreach (var layer in _outlineLayers)
        {
            layer.Text = value;
        }

        _foreground.Text = value;
    }

    private void ApplyVisualScale(double targetSize)
    {
        var scale = Math.Max(0.75, targetSize / NativePixelFontSize);
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    private static FontFamily ResolveVcrFontFamily()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "VCR_OSD_MONO.ttf");
        if (File.Exists(bundled))
        {
            return new FontFamily("ms-appx:///Assets/Fonts/VCR_OSD_MONO.ttf#VCR OSD Mono");
        }

        return new FontFamily("Consolas");
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        }

        return Color.FromArgb(
            255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}
