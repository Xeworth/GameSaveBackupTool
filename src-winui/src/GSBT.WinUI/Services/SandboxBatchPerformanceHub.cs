namespace GSBT.WinUI.Services;

public enum BatchTestRunPhase
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class BatchTestRunSnapshot
{
    public required int Index { get; init; }
    public required string Title { get; init; }
    public required string ParametersLine { get; init; }
    public BatchTestRunPhase Phase { get; set; }
    public int ProgressPercent { get; set; }
    /// <summary>Resource monitor serial when this step finished (for graph checkpoint).</summary>
    public long? CheckpointSampleSerial { get; set; }

    public SandboxCompressionBenchmarkEntry? StepResult { get; set; }
}

/// <summary>Shared batch-run state for Batch benchmark tab and Performance pane.</summary>
public sealed class SandboxBatchPerformanceHub
{
    private readonly object _lock = new();
    private List<BatchTestRunSnapshot> _tests = [];
    private bool _isActive;
    private int _activeIndex = -1;

    public event Action? StateChanged;

    /// <summary>Progress percent changed for the active step only (no full card rebuild).</summary>
    public event Action? ProgressUpdated;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _isActive;
            }
        }
    }

    public int ActiveIndex
    {
        get
        {
            lock (_lock)
            {
                return _activeIndex;
            }
        }
    }

    public IReadOnlyList<BatchTestRunSnapshot> Tests
    {
        get
        {
            lock (_lock)
            {
                return _tests.ToList();
            }
        }
    }

    public void BeginBatch(IReadOnlyList<BatchTestBeginSpec> specs)
    {
        lock (_lock)
        {
            _isActive = true;
            _activeIndex = -1;
            _tests = specs.Select((s, i) => new BatchTestRunSnapshot
            {
                Index = i,
                Title = BatchTestDisplayName.Resolve(s.DisplayName, i),
                ParametersLine = BatchTestParameterFormatter.BuildCompact(s.Preset, s.Format, s.Mx, s.Threads),
                Phase = BatchTestRunPhase.Queued,
                ProgressPercent = 0,
            }).ToList();
        }

        RaiseChanged();
    }

    public void SetStepRunning(int index)
    {
        lock (_lock)
        {
            if (!_isActive || index < 0 || index >= _tests.Count)
            {
                return;
            }

            _activeIndex = index;
            _tests[index].Phase = BatchTestRunPhase.Running;
            _tests[index].ProgressPercent = 0;
        }

        RaiseChanged();
    }

    public void SetStepProgress(int index, int percent)
    {
        lock (_lock)
        {
            if (!_isActive || index < 0 || index >= _tests.Count)
            {
                return;
            }

            _tests[index].ProgressPercent = Math.Clamp(percent, 0, 100);
        }

        ProgressUpdated?.Invoke();
    }

    public void SetStepCompleted(
        int index,
        long checkpointSampleSerial,
        SandboxCompressionBenchmarkEntry? stepResult = null)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _tests.Count)
            {
                return;
            }

            _tests[index].Phase = BatchTestRunPhase.Completed;
            _tests[index].ProgressPercent = 100;
            _tests[index].CheckpointSampleSerial = checkpointSampleSerial;
            _tests[index].StepResult = stepResult;
            if (_activeIndex == index)
            {
                _activeIndex = -1;
            }
        }

        RaiseChanged();
    }

    public SandboxCompressionBenchmarkEntry? GetStepResult(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _tests.Count)
            {
                return null;
            }

            return _tests[index].StepResult;
        }
    }

    public void SetStepFailed(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _tests.Count)
            {
                return;
            }

            _tests[index].Phase = BatchTestRunPhase.Failed;
            _activeIndex = -1;
        }

        RaiseChanged();
    }

    public void EndBatch(bool cancelled)
    {
        lock (_lock)
        {
            if (cancelled)
            {
                foreach (var t in _tests)
                {
                    if (t.Phase is BatchTestRunPhase.Queued or BatchTestRunPhase.Running)
                    {
                        t.Phase = BatchTestRunPhase.Cancelled;
                    }
                }
            }

            _isActive = false;
            _activeIndex = -1;
        }

        RaiseChanged();
    }

    private void RaiseChanged() => StateChanged?.Invoke();
}

public readonly record struct BatchTestBeginSpec(
    string Preset,
    string Format,
    int Mx,
    int Threads,
    string? DisplayName);
