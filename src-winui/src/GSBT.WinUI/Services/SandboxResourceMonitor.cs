using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GSBT.WinUI.Controls;

namespace GSBT.WinUI.Services;

/// <summary>Rolling CPU / memory samples for the sandbox Performance pane (GSBT + compression engine).</summary>
public sealed class SandboxResourceMonitor : IDisposable
{
    /// <summary>Seconds of history shown on live Performance tab sparklines.</summary>
    public const int LiveSparklineWindowSeconds = 120;

    /// <summary>Sample period while compression or other monitored work is active (4 Hz).</summary>
    public const double ActiveSampleIntervalSeconds = 0.25;

    /// <summary>Sample period when only “record when idle” is enabled (1 Hz).</summary>
    public const double IdleSampleIntervalSeconds = 1.0;

    /// <summary>Timer ticks per idle sample (4 ticks × 250 ms = 1 s).</summary>
    public const int IdleSampleTickStride = 4;

    /// <summary>Live sparkline sample cap (~2 minutes at 4 Hz).</summary>
    public const int LiveSparklineLength = 480;

    /// <summary>Alias for live sparkline window (backward compatible).</summary>
    public const int HistoryLength = LiveSparklineLength;

    /// <summary>Maximum retained samples (~30 minutes at 4 Hz).</summary>
    public const int MaxHistorySamples = 7200;

    /// <summary>Effective interval between stored samples for chart time axes.</summary>
    public const double SampleIntervalSeconds = ActiveSampleIntervalSeconds;

    public const string RecordWhenIdleSettingsKey = "sandbox_perf_record_when_idle";

    private readonly SettingsStore _store;
    private readonly SandboxMonitorSession _session;
    private readonly CompressionActivityTracker _activityTracker;
    private readonly object _lock = new();
    private readonly List<double> _appCpu = [];
    private readonly List<double> _appMemMb = [];
    private readonly List<double> _sevenZipCpu = [];
    private readonly List<double> _sevenZipMemMb = [];
    private readonly List<string> _activityLabels = [];
    private readonly List<(long Serial, string Game)> _compressionGameEvents = [];
    private long _totalSampleSerial;
    private readonly List<PerformanceChartCheckpoint> _checkpoints = [];
    private readonly List<PerformanceChartBatchSegment> _batchSegments = [];
    private (int Index, string Label, string Detail)? _pendingStepStart;
    private long _historyStartSerial;

    private TimeSpan _lastAppCpu = TimeSpan.Zero;
    private DateTime _lastAppWall = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, (TimeSpan Cpu, DateTime Wall)> _sevenZipPrev = new();

    private readonly Process _appProcess = Process.GetCurrentProcess();
    private readonly double _systemTotalPhysicalMemMb = QuerySystemTotalPhysicalMemMb();
    private Timer? _timer;
    private int _idleSampleTickCounter;

    public SandboxResourceMonitor(
        SettingsStore store,
        SandboxMonitorSession session,
        CompressionActivityTracker activityTracker)
    {
        _store = store;
        _session = session;
        _activityTracker = activityTracker;
        _activityTracker.GameFolderChanged += OnCompressionGameFolderChanged;
    }

    private void OnCompressionGameFolderChanged(string game) => RecordCompressionGameSeen(game);

    public void RecordCompressionGameSeen(string game)
    {
        if (string.IsNullOrWhiteSpace(game))
        {
            return;
        }

        var trimmed = game.Trim();
        lock (_lock)
        {
            if (_compressionGameEvents.Count > 0 &&
                _compressionGameEvents[^1].Game.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _compressionGameEvents.Add((_totalSampleSerial, trimmed));
            TrimGameEventsIfNeeded();
        }
    }

    public void CopyCompressionGameEvents(List<(long Serial, string Game)> dest)
    {
        dest.Clear();
        lock (_lock)
        {
            dest.AddRange(_compressionGameEvents);
        }
    }

    /// <summary>Installed physical RAM (MiB) for charting memory as % of system.</summary>
    public double SystemTotalPhysicalMemMb => _systemTotalPhysicalMemMb;

    public event Action? SamplesUpdated;

    public int SampleCount
    {
        get
        {
            lock (_lock)
            {
                return _appCpu.Count;
            }
        }
    }

    public long TotalSampleSerial
    {
        get
        {
            lock (_lock)
            {
                return _totalSampleSerial;
            }
        }
    }

    public long HistoryStartSerial
    {
        get
        {
            lock (_lock)
            {
                return _historyStartSerial;
            }
        }
    }

    public void ClearCheckpoints()
    {
        lock (_lock)
        {
            _checkpoints.Clear();
        }
    }

    /// <summary>Clears chart history and checkpoints at the start of a batch benchmark run.</summary>
    public void BeginBatchHistory()
    {
        lock (_lock)
        {
            _appCpu.Clear();
            _appMemMb.Clear();
            _sevenZipCpu.Clear();
            _sevenZipMemMb.Clear();
            _activityLabels.Clear();
            _compressionGameEvents.Clear();
            _checkpoints.Clear();
            _batchSegments.Clear();
            _pendingStepStart = null;
            _historyStartSerial = _totalSampleSerial + 1;
        }
    }

    /// <summary>Call when a batch step is about to start compressing (marker at spike start).</summary>
    public void NotifyBatchStepStarting(int testIndex, string shortLabel, string detailLine)
    {
        lock (_lock)
        {
            _pendingStepStart = (testIndex, shortLabel, detailLine);
        }
    }

    /// <summary>Call when a batch step finishes compressing (closes the segment range).</summary>
    public long NotifyBatchStepEnded(int testIndex)
    {
        lock (_lock)
        {
            var endSerial = _totalSampleSerial;
            if (_pendingStepStart is { Index: var pendingIdx } pending && pendingIdx == testIndex)
            {
                _batchSegments.Add(new PerformanceChartBatchSegment(
                    testIndex,
                    pending.Label,
                    pending.Detail,
                    endSerial,
                    endSerial));
                _pendingStepStart = null;
                return endSerial;
            }

            for (var i = _batchSegments.Count - 1; i >= 0; i--)
            {
                var seg = _batchSegments[i];
                if (seg.TestIndex != testIndex || seg.EndSampleSerial >= 0)
                {
                    continue;
                }

                _batchSegments[i] = seg with { EndSampleSerial = endSerial };
                return endSerial;
            }

            return endSerial;
        }
    }

    public void CopyBatchSegments(List<PerformanceChartBatchSegment> dest)
    {
        dest.Clear();
        lock (_lock)
        {
            dest.AddRange(_batchSegments);
        }
    }

    public void MapStartMarkersToRecentWindow(int recentCount, List<PerformanceChartCheckpointMarker> dest)
    {
        dest.Clear();
        if (recentCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            var total = _appCpu.Count;
            var offset = Math.Max(0, total - recentCount);
            MapStartMarkersLocked(dest, offset, recentCount);
        }
    }

    public void MapStartMarkersToHistory(int historyCount, List<PerformanceChartCheckpointMarker> dest)
    {
        dest.Clear();
        if (historyCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            MapStartMarkersLocked(dest, historyOffset: 0, historyCount);
        }
    }

    private void MapStartMarkersLocked(
        List<PerformanceChartCheckpointMarker> dest,
        int historyOffset,
        int historyCount)
    {
        var start = _historyStartSerial;
        if (_batchSegments.Count > 0)
        {
            foreach (var seg in _batchSegments)
            {
                var idx = (int)(seg.StartSampleSerial - start) - historyOffset;
                if (idx >= 0 && idx < historyCount)
                {
                    dest.Add(new PerformanceChartCheckpointMarker(idx, seg.ShortLabel, seg.DetailLine));
                }
            }

            return;
        }

        foreach (var cp in _checkpoints)
        {
            var idx = (int)(cp.SampleSerial - start) - historyOffset;
            if (idx >= 0 && idx < historyCount)
            {
                dest.Add(new PerformanceChartCheckpointMarker(idx, cp.ShortLabel, cp.DetailLine));
            }
        }
    }

    public void MapSegmentsToHistory(int historyCount, List<PerformanceChartSegmentBand> dest)
    {
        dest.Clear();
        if (historyCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            MapSegmentsLocked(dest, historyOffset: 0, historyCount);
        }
    }

    public void MapSegmentsToRecentWindow(int recentCount, List<PerformanceChartSegmentBand> dest)
    {
        dest.Clear();
        if (recentCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            var total = _appCpu.Count;
            var offset = Math.Max(0, total - recentCount);
            MapSegmentsLocked(dest, offset, recentCount);
        }
    }

    private void MapSegmentsLocked(
        List<PerformanceChartSegmentBand> dest,
        int historyOffset,
        int historyCount)
    {
        var serialStart = _historyStartSerial;
        foreach (var seg in _batchSegments)
        {
            var startIdx = (int)(seg.StartSampleSerial - serialStart) - historyOffset;
            var endIdx = seg.EndSampleSerial < seg.StartSampleSerial
                ? (int)(_totalSampleSerial - serialStart) - historyOffset
                : (int)(seg.EndSampleSerial - serialStart) - historyOffset;
            if (endIdx < 0 || startIdx >= historyCount)
            {
                continue;
            }

            startIdx = Math.Max(0, startIdx);
            endIdx = Math.Clamp(endIdx, startIdx, historyCount - 1);
            dest.Add(new PerformanceChartSegmentBand(
                seg.TestIndex,
                startIdx,
                endIdx,
                PerformanceChartPalettes.SegmentColor(seg.TestIndex),
                Visible: true));
        }
    }

    /// <summary>Legacy completion marker (non-batch).</summary>
    public long RecordCheckpoint(string shortLabel, string detailLine)
    {
        lock (_lock)
        {
            var serial = _totalSampleSerial;
            _checkpoints.Add(new PerformanceChartCheckpoint(serial, shortLabel, detailLine));
            while (_checkpoints.Count > 48)
            {
                _checkpoints.RemoveAt(0);
            }

            return serial;
        }
    }

    public void MapCheckpointsToHistory(int historyCount, List<(int Index, PerformanceChartCheckpoint Checkpoint)> dest)
    {
        dest.Clear();
        if (historyCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            MapCheckpointsLocked(dest, historyOffset: 0, historyCount);
        }
    }

    /// <summary>Maps checkpoints into the last <paramref name="recentCount"/> samples (live sparklines).</summary>
    public void MapCheckpointsToRecentWindow(
        int recentCount,
        List<(int Index, PerformanceChartCheckpoint Checkpoint)> dest)
    {
        dest.Clear();
        if (recentCount <= 0)
        {
            return;
        }

        lock (_lock)
        {
            var total = _appCpu.Count;
            var offset = Math.Max(0, total - recentCount);
            MapCheckpointsLocked(dest, offset, recentCount);
        }
    }

    private void MapCheckpointsLocked(
        List<(int Index, PerformanceChartCheckpoint Checkpoint)> dest,
        int historyOffset,
        int historyCount)
    {
        var start = _historyStartSerial;
        foreach (var cp in _checkpoints)
        {
            var idx = (int)(cp.SampleSerial - start) - historyOffset;
            if (idx >= 0 && idx < historyCount)
            {
                dest.Add((idx, cp));
            }
        }
    }

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _lastAppWall = DateTime.UtcNow;
        _lastAppCpu = _appProcess.TotalProcessorTime;
        _timer = new Timer(
            _ => SampleTick(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(ActiveSampleIntervalSeconds));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Copies all retained samples (up to each span's length).</summary>
    public int CopyHistory(
        Span<double> appCpu,
        Span<double> appMemMb,
        Span<double> sevenZipCpu,
        Span<double> sevenZipMemMb)
    {
        lock (_lock)
        {
            return CopySeriesLocked(appCpu, appMemMb, sevenZipCpu, sevenZipMemMb, startIndex: 0);
        }
    }

    /// <summary>Copies the most recent samples for live sparklines.</summary>
    public int CopyRecentHistory(
        Span<double> appCpu,
        Span<double> appMemMb,
        Span<double> sevenZipCpu,
        Span<double> sevenZipMemMb,
        int maxSamples = LiveSparklineLength)
    {
        lock (_lock)
        {
            var count = _appCpu.Count;
            if (count == 0)
            {
                appCpu.Clear();
                appMemMb.Clear();
                sevenZipCpu.Clear();
                sevenZipMemMb.Clear();
                return 0;
            }

            var start = Math.Max(0, count - maxSamples);
            return CopySeriesLocked(appCpu, appMemMb, sevenZipCpu, sevenZipMemMb, start);
        }
    }

    private int CopySeriesLocked(
        Span<double> appCpu,
        Span<double> appMemMb,
        Span<double> sevenZipCpu,
        Span<double> sevenZipMemMb,
        int startIndex)
    {
        var count = _appCpu.Count;
        if (count == 0)
        {
            appCpu.Clear();
            appMemMb.Clear();
            sevenZipCpu.Clear();
            sevenZipMemMb.Clear();
            return 0;
        }

        startIndex = Math.Clamp(startIndex, 0, count);
        var available = count - startIndex;
        var len = Math.Min(
            available,
            Math.Min(
                Math.Min(appCpu.Length, appMemMb.Length),
                Math.Min(sevenZipCpu.Length, sevenZipMemMb.Length)));

        for (var i = 0; i < len; i++)
        {
            var src = startIndex + i;
            appCpu[i] = _appCpu[src];
            appMemMb[i] = _appMemMb[src];
            sevenZipCpu[i] = _sevenZipCpu[src];
            sevenZipMemMb[i] = _sevenZipMemMb[src];
        }

        return len;
    }

    /// <summary>Copies per-sample compression activity labels (top-level game folder), aligned with <see cref="CopyHistory"/>.</summary>
    public int CopyActivityLabels(string[] dest, int startIndex = 0)
    {
        lock (_lock)
        {
            return CopyActivityLabelsLocked(dest, startIndex);
        }
    }

    /// <summary>Copies activity labels for the most recent samples (aligned with <see cref="CopyRecentHistory"/>).</summary>
    public int CopyRecentActivityLabels(string[] dest, int maxSamples = LiveSparklineLength)
    {
        lock (_lock)
        {
            var count = _activityLabels.Count;
            if (count == 0)
            {
                return 0;
            }

            var start = Math.Max(0, count - maxSamples);
            return CopyActivityLabelsLocked(dest, start);
        }
    }

    private int CopyActivityLabelsLocked(string[] dest, int startIndex)
    {
        var count = _activityLabels.Count;
        if (count == 0 || dest.Length == 0)
        {
            return 0;
        }

        startIndex = Math.Clamp(startIndex, 0, count);
        var available = count - startIndex;
        var len = Math.Min(available, dest.Length);
        for (var i = 0; i < len; i++)
        {
            dest[i] = _activityLabels[startIndex + i];
        }

        return len;
    }

    public (double AppCpu, double AppMemMb, double ZipCpu, double ZipMemMb) Latest()
    {
        lock (_lock)
        {
            if (_appCpu.Count == 0)
            {
                return (0, 0, 0, 0);
            }

            var last = _appCpu.Count - 1;
            return (_appCpu[last], _appMemMb[last], _sevenZipCpu[last], _sevenZipMemMb[last]);
        }
    }

    private void SampleTick()
    {
        try
        {
            _appProcess.Refresh();
            var now = DateTime.UtcNow;
            var cpuNow = _appProcess.TotalProcessorTime;
            var wallDelta = (now - _lastAppWall).TotalSeconds;
            var appCpuPct = 0.0;
            if (wallDelta > 0.05)
            {
                var cpuDelta = (cpuNow - _lastAppCpu).TotalMilliseconds;
                appCpuPct = cpuDelta / (wallDelta * 10.0 * Environment.ProcessorCount);
                appCpuPct = Math.Clamp(appCpuPct, 0, 100);
            }

            _lastAppWall = now;
            _lastAppCpu = cpuNow;

            var appMemMb = _appProcess.WorkingSet64 / (1024.0 * 1024.0);
            var (zipCpu, zipMemMb, sevenZipActive) = SampleSevenZipAggregate(now);
            var recordWhenIdle = _store.Get(RecordWhenIdleSettingsKey, false);
            var workloadActive = _session.IsPerformanceSamplingRelevant;
            var shouldRecord = recordWhenIdle || sevenZipActive || workloadActive;
            if (!shouldRecord)
            {
                _idleSampleTickCounter = 0;
                return;
            }

            var fastCapture = sevenZipActive || workloadActive;
            if (!fastCapture && recordWhenIdle)
            {
                _idleSampleTickCounter++;
                if (_idleSampleTickCounter < IdleSampleTickStride)
                {
                    return;
                }

                _idleSampleTickCounter = 0;
            }
            else
            {
                _idleSampleTickCounter = 0;
            }

            var compressCpu = sevenZipActive ? zipCpu : workloadActive ? appCpuPct : 0;
            var compressMem = sevenZipActive ? zipMemMb : workloadActive ? appMemMb : 0;

            var activityLabel = _activityTracker.CurrentGameFolder;

            lock (_lock)
            {
                _totalSampleSerial++;
                _appCpu.Add(appCpuPct);
                _appMemMb.Add(appMemMb);
                _sevenZipCpu.Add(compressCpu);
                _sevenZipMemMb.Add(compressMem);
                _activityLabels.Add(activityLabel);
                TrimHistoryIfNeeded();
                _historyStartSerial = _totalSampleSerial - _appCpu.Count + 1;
                TryFlushPendingStepStart();
            }

            SamplesUpdated?.Invoke();
        }
        catch
        {
            // ignore sampling failures
        }
    }

    private void TryFlushPendingStepStart()
    {
        if (_pendingStepStart is not { } pending)
        {
            return;
        }

        _batchSegments.Add(new PerformanceChartBatchSegment(
            pending.Index,
            pending.Label,
            pending.Detail,
            _totalSampleSerial,
            EndSampleSerial: -1));
        _pendingStepStart = null;
    }

    private void TrimHistoryIfNeeded()
    {
        while (_appCpu.Count > MaxHistorySamples)
        {
            _appCpu.RemoveAt(0);
            _appMemMb.RemoveAt(0);
            _sevenZipCpu.RemoveAt(0);
            _sevenZipMemMb.RemoveAt(0);
            if (_activityLabels.Count > 0)
            {
                _activityLabels.RemoveAt(0);
            }
        }

        _historyStartSerial = _totalSampleSerial - _appCpu.Count + 1;
        while (_checkpoints.Count > 0 && _checkpoints[0].SampleSerial < _historyStartSerial)
        {
            _checkpoints.RemoveAt(0);
        }

        TrimGameEventsIfNeeded();
    }

    private void TrimGameEventsIfNeeded()
    {
        while (_compressionGameEvents.Count > 0 && _compressionGameEvents[0].Serial < _historyStartSerial)
        {
            _compressionGameEvents.RemoveAt(0);
        }
    }

    private (double CpuPercent, double MemMb, bool AnyProcess) SampleSevenZipAggregate(DateTime now)
    {
        var live = new HashSet<int>();
        double memMb = 0;
        double cpuPct = 0;

        foreach (var proc in Process.GetProcessesByName("7z"))
        {
            try
            {
                live.Add(proc.Id);
                proc.Refresh();
                memMb += proc.WorkingSet64 / (1024.0 * 1024.0);

                var cpu = proc.TotalProcessorTime;
                if (_sevenZipPrev.TryGetValue(proc.Id, out var prev))
                {
                    var wall = (now - prev.Wall).TotalSeconds;
                    if (wall > 0.05)
                    {
                        var deltaMs = (cpu - prev.Cpu).TotalMilliseconds;
                        cpuPct += deltaMs / (wall * 10.0 * Environment.ProcessorCount);
                    }
                }

                _sevenZipPrev[proc.Id] = (cpu, now);
            }
            catch
            {
                // ignore dead / access denied
            }
            finally
            {
                proc.Dispose();
            }
        }

        foreach (var id in _sevenZipPrev.Keys)
        {
            if (!live.Contains(id))
            {
                _sevenZipPrev.TryRemove(id, out _);
            }
        }

        return (Math.Clamp(cpuPct, 0, 100), memMb, live.Count > 0);
    }

    public void Dispose()
    {
        _activityTracker.GameFolderChanged -= OnCompressionGameFolderChanged;
        Stop();
        _appProcess.Dispose();
    }

    private static double QuerySystemTotalPhysicalMemMb()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                return 16_384;
            }

            return status.ullTotalPhys / (1024.0 * 1024.0);
        }
        catch
        {
            return 16_384;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
