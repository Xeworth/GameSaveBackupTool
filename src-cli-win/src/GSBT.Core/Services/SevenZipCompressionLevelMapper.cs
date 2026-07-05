using SharpSevenZip;

namespace GSBT.Core.Services;

/// <summary>
/// 7-Zip / SharpSevenZip compression tiers exposed in the UI (no fake even levels).
/// </summary>
public static class SevenZipCompressionLevelMapper
{
    public static readonly int[] SupportedMxLevels = [0, 1, 3, 5, 7, 9];

    public static int SliderIndexCount => SupportedMxLevels.Length;

    public static int MxFromSliderIndex(int index) =>
        SupportedMxLevels[Math.Clamp(index, 0, SupportedMxLevels.Length - 1)];

    public static int SliderIndexFromMx(int mx)
    {
        mx = Math.Clamp(mx, 0, 9);
        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < SupportedMxLevels.Length; i++)
        {
            var distance = Math.Abs(SupportedMxLevels[i] - mx);
            if (distance < bestDistance
                || (distance == bestDistance && SupportedMxLevels[i] > SupportedMxLevels[bestIndex]))
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public static int NormalizeMx(int mx) => MxFromSliderIndex(SliderIndexFromMx(mx));

    public static CompressionLevel MapMxToCompressionLevel(int mx) =>
        NormalizeMx(mx) switch
        {
            0 => CompressionLevel.None,
            1 => CompressionLevel.Fast,
            3 => CompressionLevel.Low,
            5 => CompressionLevel.Normal,
            7 => CompressionLevel.High,
            _ => CompressionLevel.Ultra,
        };
}
