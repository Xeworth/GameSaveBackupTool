using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Controls;

/// <summary>Small screen-saver control: MDL2 icon with thick black outline (matches <see cref="VcrOutlineTextBlock"/>).</summary>
public sealed class VcrOutlineIconButton : Button
{
    private const double DefaultIconFontSize = 18.0;
    private static readonly Color HoverFill = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    private static readonly Color PressedFill = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly Color TransparentFill = Color.FromArgb(0, 0, 0, 0);

    private static readonly (int X, int Y)[] OutlineOffsets =
    [
        (-2, 0), (2, 0), (0, -2), (0, 2),
        (-2, -2), (2, -2), (-2, 2), (2, 2),
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (1, -1), (-1, 1), (1, 1),
    ];

    private readonly Grid _iconRoot;
    private readonly FontIcon[] _outlineIcons;
    private readonly FontIcon _foregroundIcon;
    private readonly SolidColorBrush _backgroundBrush;

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(
            nameof(IconGlyph),
            typeof(string),
            typeof(VcrOutlineIconButton),
            new PropertyMetadata("\uE8BB", (d, e) =>
            {
                if (d is VcrOutlineIconButton btn && e.NewValue is string glyph)
                {
                    btn.ApplyGlyph(glyph);
                }
            }));

    public static readonly DependencyProperty IconFontSizeProperty =
        DependencyProperty.Register(
            nameof(IconFontSize),
            typeof(double),
            typeof(VcrOutlineIconButton),
            new PropertyMetadata(DefaultIconFontSize, (d, e) =>
            {
                if (d is VcrOutlineIconButton btn && e.NewValue is double size)
                {
                    btn.ApplyIconSize(size);
                }
            }));

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public double IconFontSize
    {
        get => (double)GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }

    public VcrOutlineIconButton()
    {
        MinWidth = 40;
        MinHeight = 40;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(6);
        _backgroundBrush = new SolidColorBrush(TransparentFill);
        Background = _backgroundBrush;
        UseSystemFocusVisuals = false;

        _iconRoot = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _outlineIcons = new FontIcon[OutlineOffsets.Length];
        for (var i = 0; i < OutlineOffsets.Length; i++)
        {
            var (x, y) = OutlineOffsets[i];
            var icon = CreateIconLayer("#FF000000", x, y, DefaultIconFontSize);
            _outlineIcons[i] = icon;
            _iconRoot.Children.Add(icon);
        }

        _foregroundIcon = CreateIconLayer("#FFF2F5F7", 0, 0, DefaultIconFontSize);
        _iconRoot.Children.Add(_foregroundIcon);
        Content = _iconRoot;

        PointerEntered += (_, _) => SetHoverFill(HoverFill);
        PointerExited += (_, _) => SetHoverFill(TransparentFill);
        PointerCanceled += (_, _) => SetHoverFill(TransparentFill);
        PointerPressed += (_, _) => SetHoverFill(PressedFill);
        PointerReleased += (_, _) => SetHoverFill(HoverFill);

        ApplyIconSize(DefaultIconFontSize);
        ApplyGlyph("\uE8BB");
    }

    private void SetHoverFill(Color color) => _backgroundBrush.Color = color;

    private static FontIcon CreateIconLayer(string hex, double offsetX, double offsetY, double fontSize)
    {
        return new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform { X = offsetX, Y = offsetY },
            Foreground = new SolidColorBrush(ParseColor(hex)),
        };
    }

    private void ApplyGlyph(string glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return;
        }

        foreach (var icon in _outlineIcons)
        {
            icon.Glyph = glyph;
        }

        _foregroundIcon.Glyph = glyph;
    }

    private void ApplyIconSize(double size)
    {
        size = Math.Max(12, size);
        var canvas = size + 12;
        _iconRoot.Width = canvas;
        _iconRoot.Height = canvas;
        MinWidth = canvas + 14;
        MinHeight = canvas + 14;

        foreach (var icon in _outlineIcons)
        {
            icon.FontSize = size;
        }

        _foregroundIcon.FontSize = size;
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
