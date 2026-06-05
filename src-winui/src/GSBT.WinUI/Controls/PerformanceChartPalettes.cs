using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Controls;

/// <summary>Distinct line colors for CPU vs memory diagnostic charts.</summary>
public static class PerformanceChartPalettes
{
    public static readonly Color CpuGsbt = Color.FromArgb(255, 45, 140, 255);
    public static readonly Color CpuCompression = Color.FromArgb(255, 230, 180, 34);

    public static readonly Color MemGsbt = Color.FromArgb(255, 62, 200, 160);
    public static readonly Color MemCompression = Color.FromArgb(255, 210, 95, 255);

    public static SolidColorBrush CpuGsbtBrush { get; } = new(CpuGsbt);
    public static SolidColorBrush CpuCompressionBrush { get; } = new(CpuCompression);
    public static SolidColorBrush MemGsbtBrush { get; } = new(MemGsbt);
    public static SolidColorBrush MemCompressionBrush { get; } = new(MemCompression);

    public static readonly Color DiagnosticBackground = Color.FromArgb(255, 32, 32, 32);

    /// <summary>Matches <c>GsbtCardBgBrush</c> (#2D2D2D) for in-card plot areas.</summary>
    public static readonly Color CardPlotBackground = Color.FromArgb(255, 0x2d, 0x2d, 0x2d);
    public static readonly Color DiagnosticForeground = Color.FromArgb(255, 230, 230, 230);
    public static readonly Color DiagnosticSecondary = Color.FromArgb(255, 160, 160, 160);
    public static readonly Color SelectionFill = Color.FromArgb(48, 120, 170, 255);
    public static readonly Color SelectionBorder = Color.FromArgb(180, 120, 170, 255);

    private static readonly Color[] SegmentPalette =
    [
        Color.FromArgb(56, 70, 130, 255),
        Color.FromArgb(56, 255, 150, 70),
        Color.FromArgb(56, 90, 210, 120),
        Color.FromArgb(56, 210, 90, 210),
        Color.FromArgb(56, 255, 210, 70),
        Color.FromArgb(56, 70, 210, 210),
        Color.FromArgb(56, 255, 100, 140),
        Color.FromArgb(56, 160, 160, 255),
    ];

    public static Color SegmentColor(int testIndex) =>
        SegmentPalette[Math.Abs(testIndex) % SegmentPalette.Length];
}
