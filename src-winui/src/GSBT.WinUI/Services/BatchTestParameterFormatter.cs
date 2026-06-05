using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

/// <summary>One-line labels for batch benchmark rows on the Performance pane.</summary>
public static class BatchTestParameterFormatter
{
    public static string BuildCompact(string preset, string fmt, int mx, int threads)
    {
        preset = CompressionOptionsResolver.NormalizePreset(preset);
        if (preset == CompressionOptionsResolver.PresetSevenZip)
        {
            var mmt = threads == 0 ? "auto" : threads.ToString();
            return $"7-Zip · {fmt.ToUpperInvariant()} · mx{mx} · mmt{mmt}";
        }

        return preset switch
        {
            CompressionOptionsResolver.PresetStore => "ZIP · store (no compression)",
            CompressionOptionsResolver.PresetDeflateFast => "ZIP · fast deflate",
            CompressionOptionsResolver.PresetDeflateMax => "ZIP · max deflate",
            _ => "ZIP · balanced deflate",
        };
    }

    public static string BuildTitle(int index) => $"Test {index + 1}";
}
