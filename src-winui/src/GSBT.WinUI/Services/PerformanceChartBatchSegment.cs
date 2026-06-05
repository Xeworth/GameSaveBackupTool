namespace GSBT.WinUI.Services;

/// <summary>One batch benchmark step mapped to resource-monitor sample serials.</summary>
public readonly record struct PerformanceChartBatchSegment(
    int TestIndex,
    string ShortLabel,
    string DetailLine,
    long StartSampleSerial,
    long EndSampleSerial);
