using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace GSBT.WinUI.Controls;

/// <summary>
/// Hosts a <see cref="PerformanceSparkline"/> with horizontal scroll when content is wider than the viewport
/// and width scaling when the viewport grows. Ctrl + wheel zooms at the cursor; Shift + wheel pans horizontally.
/// </summary>
public sealed class ScrollablePerformanceChartHost : UserControl
{
    public const double DefaultPixelsPerSample = 7;
    public const double MinPixelsPerSample = 3;
    public const double MaxPixelsPerSample = 48;

    private const double WheelScrollPixelsPerTick = 48;

    private readonly ScrollViewer _scroll;
    private readonly PerformanceSparkline _chart;
    private int _sampleCount;
    private double _pixelsPerSample;

    public ScrollablePerformanceChartHost(
        PerformanceSparkline chart,
        int sampleCount,
        double pixelsPerSample = DefaultPixelsPerSample)
    {
        _chart = chart;
        _sampleCount = Math.Max(1, sampleCount);
        _pixelsPerSample = Math.Clamp(pixelsPerSample, MinPixelsPerSample, MaxPixelsPerSample);

        DetachChartFromVisualTree(_chart);

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 0, 0, 12),
            ZoomMode = ZoomMode.Disabled,
        };
        _scroll.Content = _chart;

        _chart.HorizontalAlignment = HorizontalAlignment.Left;

        HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = _scroll;
        Loaded += (_, _) => ApplyChartContentWidth();

        _scroll.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnPointerWheelChanged),
            handledEventsToo: true);
        _chart.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnPointerWheelChanged),
            handledEventsToo: true);
    }

    public PerformanceSparkline Chart => _chart;

    public double PixelsPerSample => _pixelsPerSample;

    public void SetSampleCount(int sampleCount)
    {
        _sampleCount = Math.Max(1, sampleCount);
        ApplyChartContentWidth();
    }

    /// <summary>Scrolls so the newest samples sit at the trailing (right) edge of the viewport.</summary>
    public void ScrollToTrailingEdge()
    {
        _scroll.UpdateLayout();
        var max = Math.Max(0, _scroll.ExtentWidth - ViewportWidth);
        _scroll.ChangeView(max, null, null, disableAnimation: true);
    }

    /// <summary>
    /// Zooms and scrolls so <paramref name="startIndex"/>…<paramref name="endIndex"/> fills the viewport
    /// (with a small margin). Does not change chart selection.
    /// </summary>
    public void ZoomToSampleRange(int startIndex, int endIndex, double viewportMargin = 0.08)
    {
        var n = _sampleCount;
        if (n < 2)
        {
            return;
        }

        var lo = Math.Clamp(Math.Min(startIndex, endIndex), 0, n - 1);
        var hi = Math.Clamp(Math.Max(startIndex, endIndex), 0, n - 1);
        var spanSamples = hi - lo + 1;
        var viewport = Math.Max(1, ViewportWidth);
        var margin = Math.Clamp(viewportMargin, 0.02, 0.25);
        var targetPps = Math.Clamp(
            viewport * (1.0 - 2 * margin) / spanSamples,
            MinPixelsPerSample,
            MaxPixelsPerSample);

        _pixelsPerSample = targetPps;
        ApplyChartContentWidth();
        _scroll.UpdateLayout();

        var contentWidth = LogicalContentWidth;
        var centerFrac = (lo + hi) / 2.0 / Math.Max(1, n - 1);
        var centerX = centerFrac * contentWidth;
        var offset = centerX - viewport / 2;
        _scroll.ChangeView(ClampHorizontalOffset(offset), null, null, disableAnimation: true);
    }

    /// <summary>Restores default horizontal zoom and scrolls to the start of the timeline.</summary>
    public void ResetView(double? pixelsPerSample = null)
    {
        _pixelsPerSample = Math.Clamp(
            pixelsPerSample ?? DefaultPixelsPerSample,
            MinPixelsPerSample,
            MaxPixelsPerSample);
        ApplyChartContentWidth();
        _scroll.UpdateLayout();
        _scroll.ChangeView(0, null, null, disableAnimation: true);
    }

    /// <param name="cursorXInScrollViewport">
    /// Horizontal pointer position within the scroll viewer viewport (pixels from the left edge).
    /// When set, zoom keeps the chart point under the cursor stationary on screen.
    /// </param>
    public void SetPixelsPerSample(double pixelsPerSample, double? cursorXInScrollViewport = null)
    {
        var oldWidth = LogicalContentWidth;
        var pointerX = cursorXInScrollViewport ?? ViewportWidth / 2;
        var oldOffset = _scroll.HorizontalOffset;
        var contentPoint = oldOffset + pointerX;

        var nextPixels = Math.Clamp(pixelsPerSample, MinPixelsPerSample, MaxPixelsPerSample);
        var newWidth = Math.Max(480, _sampleCount * nextPixels);
        if (oldWidth <= 0 || newWidth <= 0 || Math.Abs(newWidth - oldWidth) < 0.5)
        {
            _pixelsPerSample = nextPixels;
            ApplyChartContentWidth();
            return;
        }

        var scale = newWidth / oldWidth;
        var targetOffset = contentPoint * scale - pointerX;

        _pixelsPerSample = nextPixels;
        ApplyChartContentWidth(newWidth);
        _scroll.UpdateLayout();
        _scroll.ChangeView(
            ClampHorizontalOffset(targetOffset),
            null,
            null,
            disableAnimation: true);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (ctrlDown)
        {
            e.Handled = true;
            var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var factor = delta > 0 ? 1.12 : 1 / 1.12;
            var cursorX = ResolveCursorXInScrollViewport(e);
            SetPixelsPerSample(_pixelsPerSample * factor, cursorX);
            return;
        }

        if (shiftDown)
        {
            e.Handled = true;
            var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            PanHorizontally(-delta * WheelScrollPixelsPerTick / 120.0);
        }
    }

    /// <summary>Maps the wheel event to X within the scroll viewer's visible viewport.</summary>
    private double ResolveCursorXInScrollViewport(PointerRoutedEventArgs e)
    {
        try
        {
            var inScroll = e.GetCurrentPoint(_scroll).Position.X;
            if (!double.IsNaN(inScroll) && inScroll >= 0)
            {
                return Math.Clamp(inScroll, 0, Math.Max(0, ViewportWidth));
            }
        }
        catch
        {
            // fall through
        }

        return ViewportWidth / 2;
    }

    private void PanHorizontally(double deltaPixels)
    {
        if (Math.Abs(deltaPixels) < 0.01)
        {
            return;
        }

        _scroll.ChangeView(
            ClampHorizontalOffset(_scroll.HorizontalOffset + deltaPixels),
            null,
            null,
            disableAnimation: true);
    }

    private double ClampHorizontalOffset(double offset)
    {
        var max = Math.Max(0, _scroll.ExtentWidth - ViewportWidth);
        return Math.Clamp(offset, 0, max);
    }

    private double LogicalContentWidth =>
        Math.Max(480, _sampleCount * _pixelsPerSample);

    private double ViewportWidth =>
        _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth;

    private void ApplyChartContentWidth() => ApplyChartContentWidth(LogicalContentWidth);

    private void ApplyChartContentWidth(double contentWidth)
    {
        _chart.MinWidth = contentWidth;
        _chart.Width = contentWidth;
        _chart.Redraw();
    }

    private static void DetachChartFromVisualTree(PerformanceSparkline chart)
    {
        if (chart.Parent is Panel panel)
        {
            panel.Children.Remove(chart);
        }
    }
}
