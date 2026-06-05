namespace GSBT.WinUI.Services;

/// <summary>Vertical marker on Performance charts when a batch test step completes.</summary>
public readonly record struct PerformanceChartCheckpoint(
    long SampleSerial,
    string ShortLabel,
    string DetailLine);
