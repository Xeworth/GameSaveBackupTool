namespace GSBT.Core.Services;

/// <summary>Builds <see cref="CompressionOptions"/> from persisted settings.</summary>
public static class CompressionOptionsResolver
{
    public const string PresetNative7z = "seven_zip";
    public const string SolidArchiveSettingsKey = "compression_7z_solid";

    /// <param name="getInt">Settings getter: key + default int.</param>
    /// <param name="getString">Settings getter: key + default string (legacy preset migration).</param>
    /// <param name="getBool">Settings getter: key + default bool.</param>
    public static CompressionOptions FromSettings(
        Func<string, int, int> getInt,
        Func<string, string, string> getString,
        Func<string, bool, bool>? getBool = null)
    {
        var level = getInt("compression_7z_level", -1);
        if (level < 0)
        {
            level = MapLegacyPresetToLevel(getString("compression_preset", "deflate_balanced"));
        }

        var threads = getInt("compression_7z_threads", 0);
        var solid = getBool?.Invoke(SolidArchiveSettingsKey, true) ?? true;
        return Build(level, threads, solid);
    }

    /// <summary>Maps removed ZIP presets to an equivalent native 7-Zip level.</summary>
    public static int MapLegacyPresetToLevel(string? preset) =>
        (preset ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "store" => 0,
            "deflate_fast" => 3,
            "deflate_max" => 9,
            "seven_zip" => 5,
            _ => 5,
        };

    /// <summary>Builds options for sandbox batch runs without mutating settings.</summary>
    public static CompressionOptions FromExplicit(int mx, int threads = 0, bool solidArchive = true) =>
        Build(mx, threads, solidArchive);

    public static int NormalizeLevel(int mx) => SevenZipCompressionLevelMapper.NormalizeMx(mx);

    /// <summary>0 = Auto; 1..<paramref name="processorCount"/> = explicit <c>-mmt</c>.</summary>
    public static int NormalizeThreadCount(int mmt, int processorCount)
    {
        processorCount = Math.Max(1, processorCount);
        if (mmt <= 0)
        {
            return 0;
        }

        return Math.Clamp(mmt, 1, processorCount);
    }

    public static int LogicalProcessorCount => Math.Max(1, Environment.ProcessorCount);

    private static CompressionOptions Build(int mx, int mmt, bool solidArchive)
    {
        mx = NormalizeLevel(mx);
        mmt = NormalizeThreadCount(mmt, LogicalProcessorCount);
        var mmtDesc = mmt <= 0 ? "Auto" : mmt.ToString();
        var solidDesc = solidArchive ? "on" : "off";
        return new CompressionOptions(
            mx,
            mmt,
            solidArchive,
            $"7-Zip .7z LZMA2 -mx={mx} -mmt={mmtDesc} -ms={solidDesc}");
    }
}
