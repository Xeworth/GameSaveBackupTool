using Windows.UI;

namespace GSBT.WinUI.Controls;

/// <summary>Colored span on a performance chart (batch test window).</summary>
public readonly record struct PerformanceChartSegmentBand(
    int TestIndex,
    int StartIndex,
    int EndIndex,
    Color FillColor,
    bool Visible);
