using System.Linq;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;

namespace GSBT.WinUI.Views;

internal enum PerformanceChartDetailMode
{
    Combined,
    Cpu,
    Memory,
}

internal sealed class PerformanceChartLiveDetailSession : IDisposable
{
    private readonly SandboxResourceMonitor _monitor;
    private readonly SandboxBatchPerformanceHub _batchHub;
    private readonly SettingsStore _settings;
    private readonly PerformanceSparkline _chart;
    private readonly ScrollablePerformanceChartHost? _scrollHost;
    private readonly bool _includeMemoryInRange;
    private readonly PerformanceChartDetailMode _mode;
    private readonly IReadOnlyList<PerformanceChartCheckpointMarker> _fallbackCheckpoints;
    private readonly List<PerformanceChartSegmentBand> _segmentBands;
    private readonly List<(long Serial, string Game)> _gameEvents = [];
    private readonly List<PerformanceChartCheckpointMarker> _markers = [];
    private readonly Dictionary<int, bool>? _segmentSelectedByTest;
    private readonly List<BatchTestCardHost>? _batchCardHosts;
    private readonly Action? _syncBatchCardSegments;
    private readonly double[] _appCpu;
    private readonly double[] _appMem;
    private readonly double[] _compressCpu;
    private readonly double[] _compressMem;
    private readonly string[] _activityLabels;
    private readonly Window _window;
    private bool _disposed;

    public PerformanceChartLiveDetailSession(
        Window window,
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        PerformanceSparkline chart,
        ScrollablePerformanceChartHost? scrollHost,
        PerformanceChartDetailMode mode,
        IReadOnlyList<PerformanceChartCheckpointMarker> fallbackCheckpoints,
        Dictionary<int, bool>? segmentSelectedByTest,
        List<BatchTestCardHost>? batchCardHosts,
        Action? syncBatchCardSegments,
        List<PerformanceChartSegmentBand>? sharedSegmentBands = null)
    {
        _segmentBands = sharedSegmentBands ?? [];
        _window = window;
        _monitor = monitor;
        _batchHub = batchHub;
        _settings = settings;
        _chart = chart;
        _scrollHost = scrollHost;
        _mode = mode;
        _fallbackCheckpoints = fallbackCheckpoints;
        _segmentSelectedByTest = segmentSelectedByTest;
        _batchCardHosts = batchCardHosts;
        _syncBatchCardSegments = syncBatchCardSegments;
        _includeMemoryInRange = mode == PerformanceChartDetailMode.Combined || mode == PerformanceChartDetailMode.Memory;

        var cap = SandboxResourceMonitor.MaxHistorySamples;
        _appCpu = new double[cap];
        _appMem = new double[cap];
        _compressCpu = new double[cap];
        _compressMem = new double[cap];
        _activityLabels = new string[cap];

        chart.ShowCheckpoints = PerformanceChartDisplaySettings.ShowCheckpoints(CheckpointScopeForMode(mode), settings);
    }

    private static PerformanceChartCheckpointScope CheckpointScopeForMode(PerformanceChartDetailMode mode) =>
        mode switch
        {
            PerformanceChartDetailMode.Cpu => PerformanceChartCheckpointScope.Cpu,
            PerformanceChartDetailMode.Memory => PerformanceChartCheckpointScope.Ram,
            _ => PerformanceChartCheckpointScope.Combined,
        };

    public void Start()
    {
        _monitor.SamplesUpdated += OnSamplesUpdated;
        _batchHub.StateChanged += OnBatchHubChanged;
        _batchHub.ProgressUpdated += OnBatchHubProgress;
        _window.Closed += OnWindowClosed;
        RefreshChartData();
        SyncBatchCards();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e) => Dispose();

    private void OnSamplesUpdated() =>
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, RefreshChartData);

    private void OnBatchHubChanged() =>
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, SyncBatchCards);

    private void OnBatchHubProgress() =>
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, SyncBatchCardProgress);

    private void RefreshChartData()
    {
        if (_disposed)
        {
            return;
        }

        var n = _monitor.CopyHistory(_appCpu, _appMem, _compressCpu, _compressMem);
        if (n <= 0)
        {
            return;
        }

        var labelsBuf = new string[n];
        _monitor.CopyActivityLabels(labelsBuf);

        _monitor.CopyCompressionGameEvents(_gameEvents);
        _markers.Clear();
        _monitor.MapStartMarkersToHistory(n, _markers);

        _segmentBands.Clear();
        _monitor.MapSegmentsToHistory(n, _segmentBands);

        var showGsbt = PerformanceChartDisplaySettings.ShowGsbt(_settings);
        var showCompress = PerformanceChartDisplaySettings.ShowCompression(_settings);

        static double[] Slice(double[] buf, int count)
        {
            if (count >= buf.Length)
            {
                return buf;
            }

            var s = new double[count];
            Array.Copy(buf, 0, s, 0, count);
            return s;
        }

        var cpu1 = Slice(_appCpu, n);
        var cpu2 = Slice(_compressCpu, n);
        var mem1 = Slice(_appMem, n);
        var mem2 = Slice(_compressMem, n);
        var labels = new string[n];
        Array.Copy(labelsBuf, labels, n);

        _chart.HistoryStartSerial = _monitor.HistoryStartSerial;
        _chart.SampleIntervalSeconds = SandboxResourceMonitor.ActiveSampleIntervalSeconds;
        _chart.SampleActivityLabels = labels;
        _chart.CompressionGameEvents = _gameEvents;
        _chart.Checkpoints = _markers.Count > 0 ? _markers : _fallbackCheckpoints;

        switch (_mode)
        {
            case PerformanceChartDetailMode.Combined:
                _chart.Series1 = showGsbt ? cpu1 : null;
                _chart.Series2 = showCompress ? cpu2 : null;
                _chart.Series3 = showGsbt ? mem1 : null;
                _chart.Series4 = showCompress ? mem2 : null;
                break;
            case PerformanceChartDetailMode.Cpu:
                _chart.Series1 = showGsbt ? cpu1 : null;
                _chart.Series2 = showCompress ? cpu2 : null;
                _chart.RangeMemorySeries1 = showGsbt ? mem1 : null;
                _chart.RangeMemorySeries2 = showCompress ? mem2 : null;
                break;
            case PerformanceChartDetailMode.Memory:
                _chart.Series1 = showGsbt ? mem1 : null;
                _chart.Series2 = showCompress ? mem2 : null;
                break;
        }

        ApplyVisibleSegments();
        _scrollHost?.SetSampleCount(n);
        _chart.Redraw();
    }

    private void ApplyVisibleSegments()
    {
        if (_segmentSelectedByTest is null || _segmentBands.Count == 0)
        {
            _chart.TestSegments = _segmentBands.Count > 0 ? _segmentBands : null;
            return;
        }

        var visible = new List<PerformanceChartSegmentBand>();
        foreach (var band in _segmentBands)
        {
            var on = _segmentSelectedByTest.TryGetValue(band.TestIndex, out var sel) && sel;
            visible.Add(band with { Visible = on });
        }

        _chart.TestSegments = visible.Count > 0 ? visible : null;
    }

    private void SyncBatchCards()
    {
        if (_batchCardHosts is null)
        {
            return;
        }

        var tests = _batchHub.Tests;
        var active = _batchHub.ActiveIndex;
        foreach (var host in _batchCardHosts)
        {
            if (host.Index >= tests.Count)
            {
                continue;
            }

            var t = tests[host.Index];
            if (t.Index == active)
            {
                BatchTestCardBuilder.ApplyRunningState(host, t, isActive: true);
            }
            else if (_segmentSelectedByTest is not null &&
                     _segmentSelectedByTest.ContainsKey(host.Index))
            {
                var selected = _segmentSelectedByTest.TryGetValue(host.Index, out var on) && on;
                BatchTestCardBuilder.ApplySegmentSelection(
                    host,
                    selected,
                    PerformanceChartPalettes.SegmentColor(host.Index));
            }
            else
            {
                BatchTestCardBuilder.ApplyRunningState(host, t, isActive: false);
            }
        }

        _syncBatchCardSegments?.Invoke();
        ApplyVisibleSegments();
        _chart.Redraw();
    }

    private void SyncBatchCardProgress()
    {
        if (_batchCardHosts is null)
        {
            return;
        }

        var tests = _batchHub.Tests;
        var active = _batchHub.ActiveIndex;
        if (active < 0 || active >= tests.Count)
        {
            return;
        }

        var t = tests[active];
        var host = _batchCardHosts.FirstOrDefault(h => h.Index == active);
        if (host is null)
        {
            return;
        }

        BatchTestCardBuilder.ApplyRunningState(host, t, isActive: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.SamplesUpdated -= OnSamplesUpdated;
        _batchHub.StateChanged -= OnBatchHubChanged;
        _batchHub.ProgressUpdated -= OnBatchHubProgress;
        _window.Closed -= OnWindowClosed;
    }
}
