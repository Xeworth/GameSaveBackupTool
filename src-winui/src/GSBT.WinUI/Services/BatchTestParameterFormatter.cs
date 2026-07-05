using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

/// <summary>One-line labels for batch benchmark rows on the Performance pane.</summary>
public static class BatchTestParameterFormatter
{
    public static string BuildCompact(int mx, int threads, bool solidArchive = true)
    {
        mx = CompressionOptionsResolver.NormalizeLevel(mx);
        threads = CompressionOptionsResolver.NormalizeThreadCount(
            threads,
            CompressionOptionsResolver.LogicalProcessorCount);
        var mmt = threads <= 0 ? "Auto" : threads.ToString();
        var mode = solidArchive ? "Chunky" : "Smooth";
        return $"7-Zip - .7z - mx{mx} - mmt {mmt} - {mode}";
    }

    public static string BuildTitle(int index) => $"Test {index + 1}";
}
