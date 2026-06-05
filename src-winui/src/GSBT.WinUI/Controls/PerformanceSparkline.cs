using GSBT.WinUI.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace GSBT.WinUI.Controls;

/// <summary>Task-Manager-style chart with optional grid, Y-axis labels, and batch checkpoints.</summary>
public sealed class PerformanceSparkline : Grid
{
    public static readonly DependencyProperty Series1Property =
        DependencyProperty.Register(nameof(Series1), typeof(double[]), typeof(PerformanceSparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty Series2Property =
        DependencyProperty.Register(nameof(Series2), typeof(double[]), typeof(PerformanceSparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty Series1BrushProperty =
        DependencyProperty.Register(nameof(Series1Brush), typeof(Brush), typeof(PerformanceSparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty Series2BrushProperty =
        DependencyProperty.Register(nameof(Series2Brush), typeof(Brush), typeof(PerformanceSparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(PerformanceSparkline),
            new PropertyMetadata(100.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty AutoScaleProperty =
        DependencyProperty.Register(nameof(AutoScale), typeof(bool), typeof(PerformanceSparkline),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowPercentGridProperty =
        DependencyProperty.Register(nameof(ShowPercentGrid), typeof(bool), typeof(PerformanceSparkline),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty CheckpointsProperty =
        DependencyProperty.Register(nameof(Checkpoints), typeof(IReadOnlyList<PerformanceChartCheckpointMarker>), typeof(PerformanceSparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowTimeAxisProperty =
        DependencyProperty.Register(nameof(ShowTimeAxis), typeof(bool), typeof(PerformanceSparkline),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty HistoryStartSerialProperty =
        DependencyProperty.Register(nameof(HistoryStartSerial), typeof(long), typeof(PerformanceSparkline),
            new PropertyMetadata(0L, OnVisualPropertyChanged));

    public static readonly DependencyProperty SampleIntervalSecondsProperty =
        DependencyProperty.Register(nameof(SampleIntervalSeconds), typeof(double), typeof(PerformanceSparkline),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty EnableHoverInspectionProperty =
        DependencyProperty.Register(nameof(EnableHoverInspection), typeof(bool), typeof(PerformanceSparkline),
            new PropertyMetadata(false, OnHoverPropertyChanged));

    public static readonly DependencyProperty Series1LabelProperty =
        DependencyProperty.Register(nameof(Series1Label), typeof(string), typeof(PerformanceSparkline),
            new PropertyMetadata("Series 1"));

    public static readonly DependencyProperty Series2LabelProperty =
        DependencyProperty.Register(nameof(Series2Label), typeof(string), typeof(PerformanceSparkline),
            new PropertyMetadata("Series 2"));

    private static void OnHoverPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PerformanceSparkline chart)
        {
            chart.UpdateHoverCapture();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PerformanceSparkline chart)
        {
            chart.Redraw();
        }
    }

    private readonly Canvas _plotCanvas = new();
    private readonly Canvas _crosshairCanvas = new() { IsHitTestVisible = false };
    private readonly Border _mouseInterceptor = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private readonly Polyline _line1 = new() { StrokeThickness = 2, Fill = null };
    private readonly Polyline _line2 = new() { StrokeThickness = 2, Fill = null };
    private readonly Polyline _line3 = new() { StrokeThickness = 2, Fill = null, StrokeDashArray = new DoubleCollection { 4, 3 } };
    private readonly Polyline _line4 = new() { StrokeThickness = 2, Fill = null, StrokeDashArray = new DoubleCollection { 4, 3 } };
    private bool _interceptorPointersWired;
    private bool _isRangeDragging;
    private int _dragAnchorIndex = -1;
    private Point _hoverPointerPos;
    private Brush? _gridBrush;
    private Brush? _labelBrush;
    private double _chartLeft;
    private double _chartW;
    private double _chartH;
    private double _plotMax = 100;
    private int _hoverIndex = -1;

    private const double LeftMargin = 38;
    private const double RightPad = 6;
    private const double TopPad = 6;
    private const double BottomPad = 6;
    private const double TimeAxisPad = 18;

    /// <summary>Extra space below the time axis (detail scroll views).</summary>
    public double TimeAxisExtraBottomPad { get; set; }

    private bool _enableRangeSelection;

    public bool EnableRangeSelection
    {
        get => _enableRangeSelection;
        set
        {
            if (_enableRangeSelection == value)
            {
                return;
            }

            _enableRangeSelection = value;
            UpdateHoverCapture();
        }
    }

    public bool UseDiagnosticChrome { get; set; }

    /// <summary>Plot background matches performance card chrome (#2D2D2D) instead of diagnostic #202020.</summary>
    public bool UseCardPlotChrome { get; set; }

    /// <summary>When <see cref="UseCardPlotChrome"/> is set, use light or dark GSBT card palette.</summary>
    public bool DarkPlotChrome { get; set; } = true;

    public int? SelectionStartIndex { get; set; }

    public int? SelectionEndIndex { get; set; }

    public double[]? RangeMemorySeries1 { get; set; }

    public double[]? RangeMemorySeries2 { get; set; }

    public event Action<PerformanceChartRangeSummary>? RangeSelectionChanged;

    public PerformanceSparkline()
    {
        MinHeight = 120;
        MinWidth = 200;
        Children.Add(_plotCanvas);
        Children.Add(_crosshairCanvas);
        Children.Add(_mouseInterceptor);
        SizeChanged += (_, _) => Redraw();
        UpdateHoverCapture();
    }

    public void SetSelectionRange(int? start, int? end, bool raiseEvent = false)
    {
        SelectionStartIndex = start;
        SelectionEndIndex = end;
        Redraw();
        if (raiseEvent && start is not null && end is not null)
        {
            RaiseRangeSummary(start.Value, end.Value);
        }
    }

    public void ClearSelection() => SetSelectionRange(null, null, raiseEvent: true);

    public double[]? Series1
    {
        get => (double[]?)GetValue(Series1Property);
        set => SetValue(Series1Property, value);
    }

    public double[]? Series2
    {
        get => (double[]?)GetValue(Series2Property);
        set => SetValue(Series2Property, value);
    }

    public Brush? Series1Brush
    {
        get => (Brush?)GetValue(Series1BrushProperty);
        set => SetValue(Series1BrushProperty, value);
    }

    public Brush? Series2Brush
    {
        get => (Brush?)GetValue(Series2BrushProperty);
        set => SetValue(Series2BrushProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public bool ShowPercentGrid
    {
        get => (bool)GetValue(ShowPercentGridProperty);
        set => SetValue(ShowPercentGridProperty, value);
    }

    public IReadOnlyList<PerformanceChartSegmentBand>? TestSegments { get; set; }

    public IReadOnlyList<PerformanceChartCheckpointMarker>? Checkpoints
    {
        get => (IReadOnlyList<PerformanceChartCheckpointMarker>?)GetValue(CheckpointsProperty);
        set => SetValue(CheckpointsProperty, value);
    }

    public bool ShowTimeAxis
    {
        get => (bool)GetValue(ShowTimeAxisProperty);
        set => SetValue(ShowTimeAxisProperty, value);
    }

    public long HistoryStartSerial
    {
        get => (long)GetValue(HistoryStartSerialProperty);
        set => SetValue(HistoryStartSerialProperty, value);
    }

    public double SampleIntervalSeconds
    {
        get => (double)GetValue(SampleIntervalSecondsProperty);
        set => SetValue(SampleIntervalSecondsProperty, value);
    }

    public bool EnableHoverInspection
    {
        get => (bool)GetValue(EnableHoverInspectionProperty);
        set => SetValue(EnableHoverInspectionProperty, value);
    }

    public string Series1Label
    {
        get => (string)GetValue(Series1LabelProperty);
        set => SetValue(Series1LabelProperty, value);
    }

    public string Series2Label
    {
        get => (string)GetValue(Series2LabelProperty);
        set => SetValue(Series2LabelProperty, value);
    }

    /// <summary>Optional extra series shown in hover tooltips (e.g. memory when charting CPU).</summary>
    public double[]? HoverExtraSeries1 { get; set; }

    public double[]? HoverExtraSeries2 { get; set; }

    public string HoverExtraSeries1Label { get; set; } = "";

    public string HoverExtraSeries2Label { get; set; } = "";

    public double[]? Series3 { get; set; }

    public double[]? Series4 { get; set; }

    public Brush? Series3Brush { get; set; }

    public Brush? Series4Brush { get; set; }

    public string Series3Label { get; set; } = "Series 3";

    public string Series4Label { get; set; } = "Series 4";

    /// <summary>Per-sample top-level game folder being compressed (parallel to series arrays).</summary>
    public string[]? SampleActivityLabels { get; set; }

    /// <summary>Sub-second game folder transitions for range selection (serial + name).</summary>
    public IReadOnlyList<(long Serial, string Game)>? CompressionGameEvents { get; set; }

    /// <summary>When false, pink test-start markers are not drawn.</summary>
    public bool ShowCheckpoints { get; set; } = true;

    /// <summary>Plot <see cref="Series3"/>/<see cref="Series4"/> on the 0–100% axis as % of <see cref="SystemTotalMemoryMb"/>.</summary>
    public bool PlotMemoryNormalized { get; set; }

    /// <summary>Installed physical RAM (MiB); required when <see cref="PlotMemoryNormalized"/> is true.</summary>
    public double SystemTotalMemoryMb { get; set; }

    private double BottomInset => ShowTimeAxis ? BottomPad + TimeAxisPad + TimeAxisExtraBottomPad : BottomPad;

    private int SampleCount => Math.Max(
        Math.Max(Series1?.Length ?? 0, Series2?.Length ?? 0),
        Math.Max(Series3?.Length ?? 0, Series4?.Length ?? 0));

    public void Redraw()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        _plotCanvas.Children.Clear();
        if (w < 8 || h < 8)
        {
            _crosshairCanvas.Children.Clear();
            return;
        }

        if (UseCardPlotChrome)
        {
            Background = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtCardBgBrush");
            _gridBrush = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtTableGridLineBrush");
            _labelBrush = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtSecondaryLabelBrush");
        }
        else if (UseDiagnosticChrome)
        {
            Background = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtWindowBgBrush");
            _gridBrush = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtTableGridLineBrush");
            _labelBrush = ThemeBridge.GetGsbtBrush(DarkPlotChrome, "GsbtSecondaryLabelBrush");
        }
        else
        {
            Background = null;
            _gridBrush ??= new SolidColorBrush(Color.FromArgb(48, 128, 128, 128));
            _labelBrush ??= new SolidColorBrush(Color.FromArgb(180, 160, 160, 160));
        }

        var max = Math.Max(0.001, MaxValue);
        if (AutoScale)
        {
            var peak = Math.Max(0.001, Peak(Series1, Series2));
            if (SampleCount == 0)
            {
                if (SystemTotalMemoryMb > 0.001)
                {
                    peak = SystemTotalMemoryMb;
                }
                else if (MaxValue > 0.001)
                {
                    peak = MaxValue;
                }
            }

            max = peak * 1.12;
        }

        _plotMax = max;

        var chartLeft = LeftMargin;
        var chartW = Math.Max(1, w - chartLeft - RightPad);
        var chartH = Math.Max(1, h - TopPad - BottomInset);
        _chartLeft = chartLeft;
        _chartW = chartW;
        _chartH = chartH;

        if (ShowPercentGrid && !AutoScale)
        {
            DrawPercentGrid(chartLeft, chartW, chartH, max);
        }
        else if (AutoScale)
        {
            DrawAutoGrid(chartLeft, chartW, chartH, max);
        }

        DrawTestSegmentBands(chartLeft, chartW, chartH);
        DrawCheckpoints(chartLeft, chartW, chartH, max);
        DrawSelectionBand(chartLeft, chartW, chartH);

        if (ShowTimeAxis)
        {
            DrawTimeAxis(chartLeft, chartW, chartH);
        }

        _line1.Stroke = Series1Brush ?? new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);
        _line2.Stroke = Series2Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
        _line1.Points = BuildPoints(Series1, chartLeft, chartW, chartH, max);
        _line2.Points = BuildPoints(Series2, chartLeft, chartW, chartH, max);
        _plotCanvas.Children.Add(_line1);
        _plotCanvas.Children.Add(_line2);

        if (Series3 is { Length: > 0 })
        {
            _line3.Stroke = Series3Brush ?? new SolidColorBrush(PerformanceChartPalettes.MemGsbt);
            _line3.Points = BuildMemoryOverlayPoints(Series3, chartLeft, chartW, chartH, max);
            _plotCanvas.Children.Add(_line3);
        }

        if (Series4 is { Length: > 0 })
        {
            _line4.Stroke = Series4Brush ?? new SolidColorBrush(PerformanceChartPalettes.MemCompression);
            _line4.Points = BuildMemoryOverlayPoints(Series4, chartLeft, chartW, chartH, max);
            _plotCanvas.Children.Add(_line4);
        }

        RedrawHoverOverlay();
    }

    private void DrawTestSegmentBands(double chartLeft, double chartW, double chartH)
    {
        var bands = TestSegments;
        if (bands is not { Count: > 0 })
        {
            return;
        }

        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        foreach (var band in bands)
        {
            if (!band.Visible)
            {
                continue;
            }

            var lo = Math.Clamp(Math.Min(band.StartIndex, band.EndIndex), 0, n - 1);
            var hi = Math.Clamp(Math.Max(band.StartIndex, band.EndIndex), 0, n - 1);
            var x1 = chartLeft + chartW * lo / Math.Max(1, n - 1);
            var x2 = chartLeft + chartW * hi / Math.Max(1, n - 1);
            var rect = new Rectangle
            {
                Width = Math.Max(2, x2 - x1),
                Height = chartH,
                Fill = new SolidColorBrush(band.FillColor),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, TopPad);
            _plotCanvas.Children.Add(rect);
        }
    }

    private void DrawSelectionBand(double chartLeft, double chartW, double chartH)
    {
        if (SelectionStartIndex is not int a || SelectionEndIndex is not int b)
        {
            return;
        }

        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        var lo = Math.Clamp(Math.Min(a, b), 0, n - 1);
        var hi = Math.Clamp(Math.Max(a, b), 0, n - 1);
        var x1 = chartLeft + chartW * lo / Math.Max(1, n - 1);
        var x2 = chartLeft + chartW * hi / Math.Max(1, n - 1);
        var band = new Rectangle
        {
            Width = Math.Max(2, x2 - x1),
            Height = chartH,
            Fill = new SolidColorBrush(PerformanceChartPalettes.SelectionFill),
            Stroke = new SolidColorBrush(PerformanceChartPalettes.SelectionBorder),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(band, x1);
        Canvas.SetTop(band, TopPad);
        _plotCanvas.Children.Add(band);
    }

    private void RedrawHoverOverlay()
    {
        _crosshairCanvas.Children.Clear();
        if (!EnableHoverInspection || _hoverIndex < 0 || _isRangeDragging)
        {
            return;
        }

        DrawHoverCrosshair(_hoverIndex);
        DrawHoverSeriesMarkers(_hoverIndex);
        DrawHoverTooltip(_hoverIndex);
    }

    private bool IsPointerOverPlotArea(Point pos) =>
        pos.X >= _chartLeft &&
        pos.X <= _chartLeft + _chartW &&
        pos.Y >= TopPad &&
        pos.Y <= TopPad + _chartH;

    private void DrawTimeAxis(double chartLeft, double chartW, double chartH)
    {
        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        var y = TopPad + chartH + 4;
        AddGridLine(chartLeft, y, chartLeft + chartW, y);

        var elapsedEnd = (n - 1) * SampleIntervalSeconds;
        var tickCount = Math.Min(8, Math.Max(3, (int)(chartW / 72)));
        for (var t = 0; t <= tickCount; t++)
        {
            var frac = tickCount == 0 ? 0.0 : (double)t / tickCount;
            var x = chartLeft + chartW * frac;
            var secondsAgo = elapsedEnd * (1.0 - frac);
            AddGridLine(x, TopPad, x, TopPad + chartH);
            var label = new TextBlock
            {
                Text = $"{secondsAgo:0}s",
                FontSize = 9,
                Foreground = _labelBrush,
            };
            Canvas.SetLeft(label, Math.Max(0, x - 12));
            Canvas.SetTop(label, y + 2);
            _plotCanvas.Children.Add(label);
        }
    }

    private void UpdateHoverCapture()
    {
        var interactive = EnableHoverInspection || EnableRangeSelection;
        if (interactive)
        {
            WireInterceptorPointerHandlers(true);
            UpdateInteractiveCursor();
        }
        else
        {
            WireInterceptorPointerHandlers(false);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            _hoverIndex = -1;
            RedrawHoverOverlay();
        }
    }

    private void UpdateInteractiveCursor()
    {
        if (!EnableHoverInspection && !EnableRangeSelection)
        {
            return;
        }

        ProtectedCursor = InputSystemCursor.Create(
            _isRangeDragging && EnableRangeSelection
                ? InputSystemCursorShape.SizeAll
                : InputSystemCursorShape.Cross);
    }

    private void WireInterceptorPointerHandlers(bool wire)
    {
        if (wire == _interceptorPointersWired)
        {
            return;
        }

        if (wire)
        {
            _mouseInterceptor.PointerPressed += OnPlotPointerPressed;
            _mouseInterceptor.PointerMoved += OnPlotPointerMoved;
            _mouseInterceptor.PointerReleased += OnPlotPointerReleased;
            _mouseInterceptor.PointerExited += OnPlotPointerExited;
            _interceptorPointersWired = true;
        }
        else
        {
            _mouseInterceptor.PointerPressed -= OnPlotPointerPressed;
            _mouseInterceptor.PointerMoved -= OnPlotPointerMoved;
            _mouseInterceptor.PointerReleased -= OnPlotPointerReleased;
            _mouseInterceptor.PointerExited -= OnPlotPointerExited;
            _interceptorPointersWired = false;
        }
    }

    private void OnPlotPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!EnableRangeSelection)
        {
            return;
        }

        var pos = e.GetCurrentPoint(this).Position;
        if (!IsPointerOverPlotArea(pos))
        {
            return;
        }

        var idx = IndexFromX(pos.X);
        if (idx < 0)
        {
            return;
        }

        _isRangeDragging = true;
        _dragAnchorIndex = idx;
        SelectionStartIndex = idx;
        SelectionEndIndex = idx;
        _mouseInterceptor.CapturePointer(e.Pointer);
        _hoverIndex = -1;
        UpdateInteractiveCursor();
        Redraw();
        RaiseRangeSummary(idx, idx);
    }

    private void OnPlotPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isRangeDragging)
        {
            return;
        }

        _isRangeDragging = false;
        _mouseInterceptor.ReleasePointerCapture(e.Pointer);
        UpdateInteractiveCursor();
        if (SelectionStartIndex is int a && SelectionEndIndex is int b)
        {
            RaiseRangeSummary(a, b);
        }
    }

    private void OnPlotPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isRangeDragging)
        {
            return;
        }

        _hoverIndex = -1;
        RedrawHoverOverlay();
    }

    private void OnPlotPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(this).Position;
        _hoverPointerPos = pos;
        if (_isRangeDragging && EnableRangeSelection)
        {
            var idx = IndexFromX(pos.X);
            if (idx >= 0 && SelectionEndIndex != idx)
            {
                SelectionEndIndex = idx;
                Redraw();
                if (SelectionStartIndex is int a)
                {
                    RaiseRangeSummary(a, idx);
                }
            }

            return;
        }

        if (!EnableHoverInspection)
        {
            return;
        }

        if (!IsPointerOverPlotArea(pos))
        {
            if (_hoverIndex >= 0)
            {
                _hoverIndex = -1;
                RedrawHoverOverlay();
            }

            return;
        }

        var hoverIdx = IndexFromX(pos.X);
        if (hoverIdx < 0)
        {
            return;
        }

        _hoverIndex = hoverIdx;
        RedrawHoverOverlay();
    }

    private int IndexFromX(double x)
    {
        var n = SampleCount;
        if (n < 2 || _chartW < 1)
        {
            return -1;
        }

        var rel = Math.Clamp((x - _chartLeft) / _chartW, 0, 1);
        return (int)Math.Round(rel * (n - 1));
    }

    private void DrawHoverTooltip(int idx)
    {
        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        var elapsed = (n - 1 - idx) * SampleIntervalSeconds;
        var unit = ShowPercentGrid && !AutoScale ? "%" : "";
        var lines = new List<string> { $"T−{elapsed:0}s" };

        if (Series1 is { Length: > 0 } s1 && idx < s1.Length)
        {
            lines.Add($"{Series1Label}: {s1[idx]:0.#}{unit}");
        }

        if (Series2 is { Length: > 0 } s2 && idx < s2.Length)
        {
            lines.Add($"{Series2Label}: {s2[idx]:0.#}{unit}");
        }

        if (Series3 is { Length: > 0 } m1 && idx < m1.Length)
        {
            lines.Add(FormatMemoryHoverLine(Series3Label, m1[idx]));
        }

        if (Series4 is { Length: > 0 } m2 && idx < m2.Length)
        {
            lines.Add(FormatMemoryHoverLine(Series4Label, m2[idx]));
        }

        if (HoverExtraSeries1 is { Length: > 0 } e1 && idx < e1.Length &&
            !string.IsNullOrEmpty(HoverExtraSeries1Label) && Series3 is null)
        {
            lines.Add($"{HoverExtraSeries1Label}: {PerformanceChartRamFormatter.FormatSize(e1[idx])}");
        }

        if (HoverExtraSeries2 is { Length: > 0 } e2 && idx < e2.Length &&
            !string.IsNullOrEmpty(HoverExtraSeries2Label) && Series4 is null)
        {
            lines.Add($"{HoverExtraSeries2Label}: {PerformanceChartRamFormatter.FormatSize(e2[idx])}");
        }

        if (SampleActivityLabels is { Length: > 0 } labels && idx < labels.Length)
        {
            var game = labels[idx]?.Trim();
            if (!string.IsNullOrEmpty(game))
            {
                lines.Add($"Zipping: {game}");
            }
        }

        var text = string.Join('\n', lines);

        var tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 42, 42, 46)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            MaxWidth = 220,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = new SolidColorBrush(PerformanceChartPalettes.DiagnosticForeground),
            },
        };

        var tipX = Math.Clamp(_hoverPointerPos.X + 14, _chartLeft, Math.Max(_chartLeft, ActualWidth - 200));
        var tipY = Math.Clamp(_hoverPointerPos.Y - 12, TopPad, Math.Max(TopPad, TopPad + _chartH - 60));
        Canvas.SetLeft(tip, tipX);
        Canvas.SetTop(tip, tipY);
        _crosshairCanvas.Children.Add(tip);
    }

    private string FormatMemoryHoverLine(string label, double memMiB) =>
        PerformanceChartRamFormatter.FormatHoverLine(
            label,
            memMiB,
            SystemTotalMemoryMb,
            PlotMemoryNormalized);

    private PointCollection BuildMemoryOverlayPoints(double[]? samples, double chartLeft, double chartW, double chartH, double max)
    {
        if (!PlotMemoryNormalized || samples is not { Length: > 0 } || SystemTotalMemoryMb <= 0.001)
        {
            return BuildPoints(samples, chartLeft, chartW, chartH, max);
        }

        var points = new PointCollection();
        var n = samples.Length;
        for (var i = 0; i < n; i++)
        {
            var x = chartLeft + chartW * i / Math.Max(1, n - 1);
            var pctOfRam = samples[i] / SystemTotalMemoryMb * 100.0;
            var y = TopPad + chartH * (1.0 - Math.Clamp(pctOfRam, 0, max) / max);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private void RaiseRangeSummary(int start, int end)
    {
        if (AutoScale)
        {
            RangeSelectionChanged?.Invoke(PerformanceChartRangeSummary.Compute(
                start,
                end,
                SampleIntervalSeconds,
                null,
                null,
                Series1,
                Series2,
                activityLabels: SampleActivityLabels,
                compressionGameEvents: CompressionGameEvents,
                historyStartSerial: HistoryStartSerial));
            return;
        }

        var mem1 = Series3 ?? RangeMemorySeries1;
        var mem2 = Series4 ?? RangeMemorySeries2;
        RangeSelectionChanged?.Invoke(PerformanceChartRangeSummary.Compute(
            start,
            end,
            SampleIntervalSeconds,
            Series1,
            Series2,
            mem1,
            mem2,
            SampleActivityLabels,
            CompressionGameEvents,
            HistoryStartSerial));
    }

    private void DrawHoverCrosshair(int idx)
    {
        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        var x = _chartLeft + _chartW * idx / Math.Max(1, n - 1);
        _crosshairCanvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = TopPad,
            Y2 = TopPad + _chartH,
            Stroke = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
        });
    }

    private void DrawHoverSeriesMarkers(int idx)
    {
        if (GetSeriesPointAtIndex(idx, Series1, memoryNormalized: false) is Point p1)
        {
            AddHoverMarker(
                p1,
                Series1Brush ?? new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue));
        }

        if (GetSeriesPointAtIndex(idx, Series2, memoryNormalized: false) is Point p2)
        {
            AddHoverMarker(
                p2,
                Series2Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Goldenrod));
        }

        if (Series3 is { Length: > 0 } &&
            GetSeriesPointAtIndex(idx, Series3, memoryNormalized: PlotMemoryNormalized) is Point p3)
        {
            AddHoverMarker(
                p3,
                Series3Brush ?? new SolidColorBrush(PerformanceChartPalettes.MemGsbt),
                radius: 4);
        }

        if (Series4 is { Length: > 0 } &&
            GetSeriesPointAtIndex(idx, Series4, memoryNormalized: PlotMemoryNormalized) is Point p4)
        {
            AddHoverMarker(
                p4,
                Series4Brush ?? new SolidColorBrush(PerformanceChartPalettes.MemCompression),
                radius: 4);
        }
    }

    private Point? GetSeriesPointAtIndex(int idx, double[]? samples, bool memoryNormalized)
    {
        if (samples is not { Length: > 0 } || idx < 0 || idx >= samples.Length)
        {
            return null;
        }

        var n = samples.Length;
        var x = _chartLeft + _chartW * idx / Math.Max(1, n - 1);
        var max = _plotMax;
        double y;
        if (memoryNormalized && PlotMemoryNormalized && SystemTotalMemoryMb > 0.001)
        {
            var pctOfRam = samples[idx] / SystemTotalMemoryMb * 100.0;
            y = TopPad + _chartH * (1.0 - Math.Clamp(pctOfRam, 0, max) / max);
        }
        else
        {
            y = TopPad + _chartH * (1.0 - Math.Clamp(samples[idx], 0, max) / max);
        }

        return new Point(x, y);
    }

    private void AddHoverMarker(Point center, Brush fillBrush, double radius = 4.5)
    {
        var diameter = radius * 2;
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = fillBrush,
            Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ellipse, center.X - radius);
        Canvas.SetTop(ellipse, center.Y - radius);
        _crosshairCanvas.Children.Add(ellipse);
    }

    private void DrawPercentGrid(double chartLeft, double chartW, double chartH, double max)
    {
        var ticks = new[] { 100.0, 75.0, 50.0, 25.0, 0.0 };
        foreach (var pct in ticks)
        {
            var y = TopPad + chartH * (1.0 - pct / 100.0);
            AddGridLine(chartLeft, y, chartLeft + chartW, y);
            AddYLabel($"{pct:0}%", y);
        }
    }

    private void DrawAutoGrid(double chartLeft, double chartW, double chartH, double max)
    {
        for (var i = 4; i >= 0; i--)
        {
            var v = max * i / 4.0;
            var y = TopPad + chartH * (1.0 - v / max);
            AddGridLine(chartLeft, y, chartLeft + chartW, y);
            AddYLabel(FormatAutoTick(v), y);
        }
    }

    private static string FormatAutoTick(double v)
    {
        if (v >= 1024)
        {
            return $"{v / 1024:0.#}k";
        }

        return $"{v:0.#}";
    }

    private void DrawCheckpoints(double chartLeft, double chartW, double chartH, double max)
    {
        if (!ShowCheckpoints)
        {
            return;
        }

        var cps = Checkpoints;
        if (cps is not { Count: > 0 })
        {
            return;
        }

        var n = SampleCount;
        if (n < 2)
        {
            return;
        }

        var accent = new SolidColorBrush(Color.FromArgb(220, 255, 120, 180));
        foreach (var marker in cps)
        {
            var idx = marker.HistoryIndex;
            if (idx < 0 || idx >= n)
            {
                continue;
            }

            var x = chartLeft + chartW * idx / Math.Max(1, n - 1);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopPad,
                Y2 = TopPad + chartH,
                Stroke = accent,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            };
            _plotCanvas.Children.Add(line);

            var tag = new Border
            {
                Background = accent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = marker.ShortLabel,
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            Canvas.SetLeft(tag, Math.Clamp(x - 14, chartLeft, chartLeft + chartW - 28));
            Canvas.SetTop(tag, TopPad);
            _plotCanvas.Children.Add(tag);
        }
    }

    private void AddGridLine(double x1, double y, double x2, double y2)
    {
        _plotCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y,
            X2 = x2,
            Y2 = y2,
            Stroke = _gridBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
        });
    }

    private void AddYLabel(string text, double y)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = _labelBrush,
        };
        Canvas.SetLeft(tb, 2);
        Canvas.SetTop(tb, y - 8);
        _plotCanvas.Children.Add(tb);
    }

    private static double Peak(params double[]?[] series)
    {
        var peak = 1.0;
        foreach (var s in series)
        {
            if (s is null)
            {
                continue;
            }

            foreach (var v in s)
            {
                if (v > peak)
                {
                    peak = v;
                }
            }
        }

        return peak;
    }

    private static PointCollection BuildPoints(double[]? samples, double chartLeft, double chartW, double chartH, double max)
    {
        var points = new PointCollection();
        if (samples is not { Length: > 0 })
        {
            return points;
        }

        var n = samples.Length;
        for (var i = 0; i < n; i++)
        {
            var x = chartLeft + chartW * i / Math.Max(1, n - 1);
            var y = TopPad + chartH * (1.0 - Math.Clamp(samples[i], 0, max) / max);
            points.Add(new Point(x, y));
        }

        return points;
    }
}

/// <summary>Checkpoint with index in the current displayed history window.</summary>
public readonly record struct PerformanceChartCheckpointMarker(
    int HistoryIndex,
    string ShortLabel,
    string DetailLine);
