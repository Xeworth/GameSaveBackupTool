namespace GSBT.WinUI.Services;

/// <summary>Cross-window sandbox monitor state (e.g. batch run in progress) for exit guardrails.</summary>
public sealed class SandboxMonitorSession
{
    private volatile int _batchBenchmarkRunning;
    private volatile int _compressionWorkloadActive;

    public bool IsBatchBenchmarkRunning => _batchBenchmarkRunning != 0;

    public bool IsCompressionWorkloadActive => _compressionWorkloadActive != 0;

    public bool IsPerformanceSamplingRelevant => IsBatchBenchmarkRunning || IsCompressionWorkloadActive;

    public void SetBatchBenchmarkRunning(bool running) =>
        _ = Interlocked.Exchange(ref _batchBenchmarkRunning, running ? 1 : 0);

    public void SetCompressionWorkloadActive(bool active) =>
        _ = Interlocked.Exchange(ref _compressionWorkloadActive, active ? 1 : 0);
}
