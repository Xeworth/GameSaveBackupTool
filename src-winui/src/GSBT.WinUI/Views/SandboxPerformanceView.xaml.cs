using System.Linq;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxPerformanceView : UserControl
{
    private static readonly BatchTestCardLayoutOptions CardLayout = new(MaxCardColumns: 3);

    private readonly SandboxResourceMonitor _monitor;
    private readonly SandboxBatchPerformanceHub _batchHub;
    private readonly SettingsStore _settings;
    private readonly Window _ownerWindow;
    private readonly double[] _appCpu = new double[SandboxResourceMonitor.HistoryLength];
    private readonly double[] _appMem = new double[SandboxResourceMonitor.HistoryLength];
    private readonly double[] _zipCpu = new double[SandboxResourceMonitor.HistoryLength];
    private readonly double[] _zipMem = new double[SandboxResourceMonitor.HistoryLength];
    private readonly List<PerformanceChartCheckpointMarker> _chartMarkers = new();
    private readonly List<PerformanceChartSegmentBand> _segmentBandScratch = new();
    private readonly List<BatchTestCardHost> _cardHosts = new();
    private readonly Dictionary<int, bool> _segmentSelected = new();
    private int _lastRecentSampleCount;

    /// <summary>Minimum horizontal spacing between points once the viewport is full (no further squeeze).</summary>
    private const double PaneMinPointSpacing = 4.0;

    private const double PaneChartHorizontalPad = 44;

    public SandboxPerformanceView(
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        Window ownerWindow)
    {
        _monitor = monitor;
        _batchHub = batchHub;
        _settings = settings;
        _ownerWindow = ownerWindow;
        InitializeComponent();

        CpuChart.Series1Brush = PerformanceChartPalettes.CpuGsbtBrush;
        CpuChart.Series2Brush = PerformanceChartPalettes.CpuCompressionBrush;
        MemChart.Series1Brush = PerformanceChartPalettes.MemGsbtBrush;
        MemChart.Series2Brush = PerformanceChartPalettes.MemCompressionBrush;
        MemChart.SystemTotalMemoryMb = monitor.SystemTotalPhysicalMemMb;
        CombinedChart.Series1Brush = PerformanceChartPalettes.CpuGsbtBrush;
        CombinedChart.Series2Brush = PerformanceChartPalettes.CpuCompressionBrush;
        CombinedChart.Series3Brush = PerformanceChartPalettes.MemGsbtBrush;
        CombinedChart.Series4Brush = PerformanceChartPalettes.MemCompressionBrush;
        CombinedChart.PlotMemoryNormalized = true;
        CombinedChart.SystemTotalMemoryMb = monitor.SystemTotalPhysicalMemMb;
        CombinedChart.UseCardPlotChrome = true;
        CombinedChart.EnableHoverInspection = false;
        CombinedChart.EnableRangeSelection = false;

        WirePaneCheckpointToggles();

        Loaded += SandboxPerformanceView_Loaded;
        Unloaded += SandboxPerformanceView_Unloaded;
        ActualThemeChanged += (_, _) => RefreshChromeTheme();
    }

    /// <summary>Called from <see cref="SandboxMonitorWindow.ApplyShellChromeTheme"/> for immediate theme sync.</summary>
    public void OnShellThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        RefreshChromeTheme();
    }

    private void RefreshChromeTheme()
    {
        var dark = ThemeBridge.IsDarkChrome(this);
        CombinedChart.DarkPlotChrome = dark;
        CpuChart.DarkPlotChrome = dark;
        MemChart.DarkPlotChrome = dark;
        CombinedChart.Redraw();
        CpuChart.Redraw();
        MemChart.Redraw();

        foreach (var host in _cardHosts)
        {
            BatchTestCardBuilder.ApplyChromeTheme(host, dark);
        }

        ApplySegmentCardVisuals();
    }

    private int EstimateMaxVisibleSamples(PerformanceSparkline chart)
    {
        var w = chart.ActualWidth;
        if (w < 16)
        {
            return 80;
        }

        return Math.Max(2, (int)((w - PaneChartHorizontalPad) / PaneMinPointSpacing) + 1);
    }

    private static double[] TrimBuffer(double[] buf, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<double>();
        }

        if (count >= buf.Length)
        {
            return buf;
        }

        var slice = new double[count];
        Array.Copy(buf, 0, slice, 0, count);
        return slice;
    }

    private static double[] SliceTrailing(double[] buf, int totalCount, int displayCount)
    {
        if (displayCount <= 0)
        {
            return Array.Empty<double>();
        }

        if (totalCount <= displayCount)
        {
            return TrimBuffer(buf, totalCount);
        }

        var start = totalCount - displayCount;
        var slice = new double[displayCount];
        Array.Copy(buf, start, slice, 0, displayCount);
        return slice;
    }

    private int PreparePaneDisplayCount(int rawCount, PerformanceSparkline chart)
    {
        if (rawCount <= 0)
        {
            return 0;
        }

        var maxVisible = EstimateMaxVisibleSamples(chart);
        return Math.Min(rawCount, maxVisible);
    }

    private void WirePaneCheckpointToggles()
    {
        AttachCheckpointToggle(CombinedHeaderGrid, CombinedChart, PerformanceChartCheckpointScope.Combined);
        AttachCheckpointToggle(CpuHeaderGrid, CpuChart, PerformanceChartCheckpointScope.Cpu);
        AttachCheckpointToggle(MemHeaderGrid, MemChart, PerformanceChartCheckpointScope.Ram);
    }

    private IReadOnlyList<PerformanceChartCheckpointMarker>? CheckpointsForChart(PerformanceChartCheckpointScope scope) =>
        PerformanceChartDisplaySettings.ShowCheckpoints(scope, _settings) && _chartMarkers.Count > 0
            ? _chartMarkers
            : null;

    private void AttachCheckpointToggle(Grid header, PerformanceSparkline chart, PerformanceChartCheckpointScope scope)
    {
        var btn = PerformanceChartHeaderChrome.CreateCheckpointToggleButton(chart, _settings, scope);
        Grid.SetColumn(btn, 2);
        header.Children.Add(btn);
    }

    private void SandboxPerformanceView_Loaded(object sender, RoutedEventArgs e)
    {
        CombinedChart.SizeChanged += PaneChart_SizeChanged;
        CpuChart.SizeChanged += PaneChart_SizeChanged;
        MemChart.SizeChanged += PaneChart_SizeChanged;
        ThemeBridge.ShellThemeChanged += OnGlobalShellThemeChanged;
        _monitor.SamplesUpdated += Monitor_SamplesUpdated;
        _batchHub.StateChanged += BatchHub_StateChanged;
        _batchHub.ProgressUpdated += BatchHub_ProgressUpdated;
        RefreshCharts();
        RefreshSamplingStatusCaption();
        SyncBatchCards(fullStructureRebuild: true);
        RefreshChromeTheme();
    }

    private void PaneChart_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, RefreshCharts);

    private void SandboxPerformanceView_Unloaded(object sender, RoutedEventArgs e)
    {
        CombinedChart.SizeChanged -= PaneChart_SizeChanged;
        CpuChart.SizeChanged -= PaneChart_SizeChanged;
        MemChart.SizeChanged -= PaneChart_SizeChanged;
        ThemeBridge.ShellThemeChanged -= OnGlobalShellThemeChanged;
        _monitor.SamplesUpdated -= Monitor_SamplesUpdated;
        _batchHub.StateChanged -= BatchHub_StateChanged;
        _batchHub.ProgressUpdated -= BatchHub_ProgressUpdated;
    }

    private void OnGlobalShellThemeChanged(ElementTheme theme) =>
        DispatcherQueue.TryEnqueue(() => OnShellThemeChanged(theme));

    private void Monitor_SamplesUpdated() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            RefreshCharts();
            RefreshSamplingStatusCaption();
        });

    private void BatchHub_StateChanged() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            SyncBatchCards(fullStructureRebuild: NeedsStructureRebuild()));

    private void BatchHub_ProgressUpdated() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, UpdateBatchCardsInPlace);

    private void BatchTestCardsHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RelayoutBatchCardsWrap();

    private bool NeedsStructureRebuild()
    {
        var tests = _batchHub.Tests;
        if (tests.Count != _cardHosts.Count)
        {
            return true;
        }

        for (var i = 0; i < tests.Count; i++)
        {
            var host = _cardHosts[i];
            var t = tests[i];
            if (host.Index != t.Index ||
                host.TitleKey != t.Title ||
                host.ParamsKey != t.ParametersLine)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshCharts()
    {
        var n = _monitor.CopyRecentHistory(_appCpu, _appMem, _zipCpu, _zipMem);
        _lastRecentSampleCount = n;

        var cpuDisplay = PreparePaneDisplayCount(n, CpuChart);
        var memDisplay = PreparePaneDisplayCount(n, MemChart);
        var combinedDisplay = PreparePaneDisplayCount(n, CombinedChart);
        var cpu1 = n > 0 ? SliceTrailing(_appCpu, n, cpuDisplay) : Array.Empty<double>();
        var cpu2 = n > 0 ? SliceTrailing(_zipCpu, n, cpuDisplay) : Array.Empty<double>();
        var mem1 = n > 0 ? SliceTrailing(_appMem, n, memDisplay) : Array.Empty<double>();
        var mem2 = n > 0 ? SliceTrailing(_zipMem, n, memDisplay) : Array.Empty<double>();

        _chartMarkers.Clear();
        if (cpuDisplay > 0)
        {
            _monitor.MapStartMarkersToRecentWindow(cpuDisplay, _chartMarkers);
        }

        var showGsbt = PerformanceChartDisplaySettings.ShowGsbt(_settings);
        var showCompress = PerformanceChartDisplaySettings.ShowCompression(_settings);

        CpuChart.Series1 = showGsbt ? cpu1 : null;
        CpuChart.Series2 = showCompress ? cpu2 : null;
        CpuChart.Checkpoints = CheckpointsForChart(PerformanceChartCheckpointScope.Cpu);
        MemChart.Series1 = showGsbt ? mem1 : null;
        MemChart.Series2 = showCompress ? mem2 : null;
        MemChart.Checkpoints = CheckpointsForChart(PerformanceChartCheckpointScope.Ram);
        CpuChart.Redraw();
        MemChart.Redraw();

        CpuLegendGsbt.Visibility = showGsbt ? Visibility.Visible : Visibility.Collapsed;
        CpuLegendCompression.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
        MemLegendGsbt.Visibility = showGsbt ? Visibility.Visible : Visibility.Collapsed;
        MemLegendCompression.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
        CpuLegendCheckpoint.Visibility = _chartMarkers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (n > 0)
        {
            var latest = _monitor.Latest();
            var cpuParts = new List<string>();
            if (showGsbt)
            {
                cpuParts.Add($"GSBT {latest.AppCpu:0.#}%");
            }

            if (showCompress)
            {
                cpuParts.Add($"Compression {latest.ZipCpu:0.#}%");
            }

            if (_chartMarkers.Count > 0)
            {
                cpuParts.Add($"{_chartMarkers.Count} checkpoint(s) on chart");
            }

            CpuCaption.Text = cpuParts.Count > 0 ? $"Now: {string.Join(" · ", cpuParts)}" : "";

            var memParts = new List<string>();
            if (showGsbt)
            {
                memParts.Add($"GSBT {PerformanceChartRamFormatter.FormatSize(latest.AppMemMb)}");
            }

            if (showCompress)
            {
                memParts.Add($"Compression {PerformanceChartRamFormatter.FormatSize(latest.ZipMemMb)}");
            }

            MemCaption.Text = memParts.Count > 0 ? $"Now: {string.Join(" · ", memParts)}" : "";
        }
        else
        {
            CpuCaption.Text = showGsbt || showCompress
                ? "Waiting for samples (start a batch or enable record-when-idle in Monitor settings)."
                : "";
            MemCaption.Text = CpuCaption.Text;
        }

        EnsureSegmentSelectionInitialized();
        RefreshCombinedChart(combinedDisplay, cpu1, cpu2, mem1, mem2, showGsbt, showCompress);
        ApplySegmentCardVisuals();
    }

    private void RefreshCombinedChart(
        int displayCount,
        double[] appCpu,
        double[] compressCpu,
        double[] appMem,
        double[] compressMem,
        bool showGsbt,
        bool showCompress)
    {
        var hasSeries = showGsbt || showCompress;
        CombinedChartSection.Visibility = hasSeries ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSeries)
        {
            return;
        }

        CombinedChart.Series1 = showGsbt ? appCpu : null;
        CombinedChart.Series2 = showCompress ? compressCpu : null;
        CombinedChart.Series3 = showGsbt ? appMem : null;
        CombinedChart.Series4 = showCompress ? compressMem : null;
        CombinedChart.Checkpoints = CheckpointsForChart(PerformanceChartCheckpointScope.Combined);

        _segmentBandScratch.Clear();
        _monitor.MapSegmentsToRecentWindow(displayCount, _segmentBandScratch);
        var visibleBands = new List<PerformanceChartSegmentBand>();
        foreach (var band in _segmentBandScratch)
        {
            var on = _segmentSelected.TryGetValue(band.TestIndex, out var sel) && sel;
            visibleBands.Add(band with { Visible = on });
        }

        CombinedChart.DarkPlotChrome = ThemeBridge.IsDarkChrome(this);
        CombinedChart.TestSegments = visibleBands.Count > 0 ? visibleBands : null;
        CombinedChart.Redraw();

        CombinedLegendGsbtCpu.Visibility = showGsbt ? Visibility.Visible : Visibility.Collapsed;
        CombinedLegendCompressCpu.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
        CombinedLegendGsbtMem.Visibility = showGsbt ? Visibility.Visible : Visibility.Collapsed;
        CombinedLegendCompressMem.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
        CombinedLegendCheckpoint.Visibility = _chartMarkers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var parts = new List<string>();
        if (showGsbt && showCompress)
        {
            parts.Add("CPU % and RAM (% of installed RAM, dashed)");
        }
        else if (showCompress)
        {
            parts.Add("Compression CPU % and RAM");
        }
        else
        {
            parts.Add("GSBT CPU % and RAM");
        }

        if (_segmentBandScratch.Count > 0)
        {
            parts.Add("click batch cards to toggle segment highlights");
        }

        CombinedCaption.Text = string.Join(" · ", parts);
    }

    private void EnsureSegmentSelectionInitialized()
    {
        var segments = new List<PerformanceChartBatchSegment>();
        _monitor.CopyBatchSegments(segments);
        var indices = new HashSet<int>();
        foreach (var seg in segments)
        {
            indices.Add(seg.TestIndex);
        }

        var stale = _segmentSelected.Keys.Where(k => !indices.Contains(k)).ToList();
        foreach (var key in stale)
        {
            _segmentSelected.Remove(key);
        }

        foreach (var idx in indices)
        {
            if (!_segmentSelected.ContainsKey(idx))
            {
                _segmentSelected[idx] = false;
            }
        }
    }

    private void ApplySegmentCardVisuals()
    {
        var tests = _batchHub.Tests;
        var active = _batchHub.ActiveIndex;
        var hasSegments = RefreshSegmentScratch();

        foreach (var host in _cardHosts)
        {
            if (host.Index >= tests.Count)
            {
                continue;
            }

            var t = tests[host.Index];
            if (t.Index == active)
            {
                BatchTestCardBuilder.ApplyRunningState(host, t, isActive: true);
                continue;
            }

            if (hasSegments && TryGetRecentSegmentForTest(host.Index, out _))
            {
                var selected = _segmentSelected.TryGetValue(host.Index, out var on) && on;
                BatchTestCardBuilder.ApplySegmentSelection(
                    host,
                    selected,
                    PerformanceChartPalettes.SegmentColor(host.Index));
                continue;
            }

            if (hasSegments && HasSegmentForTest(host.Index) && !TryGetRecentSegmentForTest(host.Index, out _))
            {
                BatchTestCardBuilder.ApplyRunningState(host, t, isActive: false);
                BatchTestCardBuilder.ApplyDisabled(host, disabled: true);
                continue;
            }

            BatchTestCardBuilder.ApplyDisabled(host, disabled: false);
            BatchTestCardBuilder.ApplyRunningState(host, t, isActive: false);
        }
    }

    private bool RefreshSegmentScratch()
    {
        _segmentBandScratch.Clear();
        if (_lastRecentSampleCount <= 0)
        {
            return false;
        }

        var displayCount = Math.Max(
            PreparePaneDisplayCount(_lastRecentSampleCount, CombinedChart),
            Math.Max(
                PreparePaneDisplayCount(_lastRecentSampleCount, CpuChart),
                PreparePaneDisplayCount(_lastRecentSampleCount, MemChart)));
        _monitor.MapSegmentsToRecentWindow(displayCount, _segmentBandScratch);
        return _segmentBandScratch.Count > 0;
    }

    private bool HasSegmentForTest(int testIndex)
    {
        var segments = new List<PerformanceChartBatchSegment>();
        _monitor.CopyBatchSegments(segments);
        return segments.Any(s => s.TestIndex == testIndex);
    }

    private bool TryGetRecentSegmentForTest(int testIndex, out PerformanceChartSegmentBand band)
    {
        foreach (var b in _segmentBandScratch)
        {
            if (b.TestIndex == testIndex)
            {
                band = b;
                return true;
            }
        }

        band = default;
        return false;
    }

    private void RefreshSamplingStatusCaption()
    {
        if (_monitor.SampleCount == 0)
        {
            SamplingStatusText.Visibility = Visibility.Visible;
            SamplingStatusText.Text =
                "Charts update only while compression is running. Enable “Record charts when idle” in Monitor settings to sample continuously.";
        }
        else
        {
            SamplingStatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void SyncBatchCards(bool fullStructureRebuild)
    {
        var tests = _batchHub.Tests;
        var show = tests.Count > 0;
        BatchOverviewSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _cardHosts.Clear();
            BatchTestCardsHost.Children.Clear();
            BatchTestCardsHost.RowDefinitions.Clear();
            BatchTestCardsHost.ColumnDefinitions.Clear();
            return;
        }

        if (fullStructureRebuild)
        {
            RebuildBatchCardStructure(tests);
        }

        UpdateBatchOverviewCaption();
        UpdateBatchCardsInPlace();
        RelayoutBatchCardsWrap();
    }

    private void UpdateBatchOverviewCaption()
    {
        var tests = _batchHub.Tests;
        var running = tests.Count(t => t.Phase == BatchTestRunPhase.Running);
        var done = tests.Count(t => t.Phase == BatchTestRunPhase.Completed);
        var segCheck = new List<PerformanceChartBatchSegment>();
        _monitor.CopyBatchSegments(segCheck);
        var segmentHint = segCheck.Count > 0
            ? " Click cards to toggle combined-chart segment highlights."
            : string.Empty;
        BatchOverviewCaption.Text = _batchHub.IsActive
            ? $"Step {Math.Min(done + running, tests.Count)} of {tests.Count} · watch compression progress and resource graphs below.{segmentHint}"
            : $"Last batch: {done} completed.{segmentHint}";
    }

    private void RebuildBatchCardStructure(IReadOnlyList<BatchTestRunSnapshot> tests)
    {
        _cardHosts.Clear();
        BatchTestCardsHost.Children.Clear();
        var dark = ThemeBridge.IsDarkChrome(this);

        foreach (var t in tests)
        {
            var host = BatchTestCardBuilder.Create(t, dark);
            WireSegmentCardClick(host);
            _cardHosts.Add(host);
        }
    }

    private void WireSegmentCardClick(BatchTestCardHost host)
    {
        host.Root.PointerPressed += (_, e) =>
        {
            if (!BatchTestCardBuilder.IsPrimaryPointerPressed(e))
            {
                return;
            }

            RefreshSegmentScratch();
            if (!HasSegmentForTest(host.Index) ||
                !TryGetRecentSegmentForTest(host.Index, out PerformanceChartSegmentBand _))
            {
                return;
            }

            var nowOn = !(_segmentSelected.TryGetValue(host.Index, out var sel) && sel);
            _segmentSelected[host.Index] = nowOn;
            ApplySegmentCardVisuals();
            if (_lastRecentSampleCount > 0)
            {
                RefreshCharts();
            }

            e.Handled = true;
        };
    }

    private void UpdateBatchCardsInPlace()
    {
        var tests = _batchHub.Tests;
        if (tests.Count == 0 || _cardHosts.Count != tests.Count)
        {
            return;
        }

        var active = _batchHub.ActiveIndex;
        var dark = ThemeBridge.IsDarkChrome(this);
        for (var i = 0; i < tests.Count; i++)
        {
            var t = tests[i];
            var host = _cardHosts[i];
            BatchTestCardBuilder.ApplyRunningState(host, t, t.Index == active);
        }

        ApplySegmentCardVisuals();
    }

    private void RelayoutBatchCardsWrap() =>
        BatchTestCardBuilder.RelayoutCards(BatchTestCardsHost, _cardHosts, CardLayout);

    private async void CpuExpandButton_Click(object sender, RoutedEventArgs e) =>
        await PerformanceChartDetailDialog.ShowCpuAsync(
                this,
                _ownerWindow,
                _monitor,
                _batchHub,
                _settings,
                _chartMarkers)
            .ConfigureAwait(true);

    private async void MemExpandButton_Click(object sender, RoutedEventArgs e) =>
        await PerformanceChartDetailDialog.ShowMemoryAsync(
                this,
                _ownerWindow,
                _monitor,
                _batchHub,
                _settings,
                _chartMarkers)
            .ConfigureAwait(true);

    private async void OverlayChartButton_Click(object sender, RoutedEventArgs e) =>
        await PerformanceChartDetailDialog.ShowOverlayAsync(
                this,
                _ownerWindow,
                _monitor,
                _batchHub,
                _settings,
                _chartMarkers)
            .ConfigureAwait(true);
}
