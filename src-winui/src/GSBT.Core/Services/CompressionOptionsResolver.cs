namespace GSBT.Core.Services;

/// <summary>Builds <see cref="CompressionOptions"/> from persisted settings (Python <c>options_from_qsettings</c>).</summary>
public static class CompressionOptionsResolver
{
    public const string PresetStore = "store";
    public const string PresetDeflateFast = "deflate_fast";
    public const string PresetDeflateBalanced = "deflate_balanced";
    public const string PresetDeflateMax = "deflate_max";
    public const string PresetSevenZip = "seven_zip";

    /// <param name="getString">Settings getter: key + default string.</param>
    /// <param name="getInt">Settings getter: key + default int.</param>
    public static CompressionOptions FromSettings(Func<string, string, string> getString, Func<string, int, int> getInt)
    {
        var preset = NormalizePreset(getString("compression_preset", PresetDeflateBalanced));
        var mx = Math.Clamp(getInt("compression_7z_level", 5), 0, 9);
        var mmt = Math.Clamp(getInt("compression_7z_threads", 0), 0, 256);
        var zfmt = Normalize7zFormat(getString("compression_7z_format", "7z"));
        var seven = preset == PresetSevenZip
            ? SevenZipLocator.ResolveSevenZipExe(getString("compression_7z_path", string.Empty))
            : null;

        return preset switch
        {
            PresetStore => new CompressionOptions(
                "zipfile",
                CompressionKind.Stored,
                0,
                null,
                "zip",
                mx,
                mmt,
                "ZIP store (no compression, minimal CPU)"),
            PresetDeflateFast => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                1,
                null,
                "zip",
                mx,
                mmt,
                "Built-in ZIP deflate level 1 (single core, fast)"),
            PresetDeflateMax => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                9,
                null,
                "zip",
                mx,
                mmt,
                "Built-in ZIP deflate level 9 (single core, heavy)"),
            PresetSevenZip => BuildSevenZipOptions(seven, zfmt, mx, mmt),
            _ => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                6,
                null,
                "zip",
                mx,
                mmt,
                "Built-in ZIP deflate level 6 (single-thread)"),
        };
    }

    /// <summary>Builds options for sandbox batch runs without mutating settings (same rules as <see cref="FromSettings"/>).</summary>
    public static CompressionOptions FromExplicit(
        string preset,
        string sevenArchiveFormat,
        int mx,
        int threads,
        string? compression7zPathFromSettings)
    {
        var p = NormalizePreset(preset);
        var mxClamped = Math.Clamp(mx, 0, 9);
        var mmt = Math.Clamp(threads, 0, 256);
        var zfmt = Normalize7zFormat(sevenArchiveFormat);
        var seven = p == PresetSevenZip
            ? SevenZipLocator.ResolveSevenZipExe(compression7zPathFromSettings ?? string.Empty)
            : null;

        return p switch
        {
            PresetStore => new CompressionOptions(
                "zipfile",
                CompressionKind.Stored,
                0,
                null,
                "zip",
                mxClamped,
                mmt,
                "ZIP store (no compression, minimal CPU)"),
            PresetDeflateFast => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                1,
                null,
                "zip",
                mxClamped,
                mmt,
                "Built-in ZIP deflate level 1 (single core, fast)"),
            PresetDeflateMax => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                9,
                null,
                "zip",
                mxClamped,
                mmt,
                "Built-in ZIP deflate level 9 (single core, heavy)"),
            PresetSevenZip => BuildSevenZipOptions(seven, zfmt, mxClamped, mmt),
            _ => new CompressionOptions(
                "zipfile",
                CompressionKind.Deflated,
                6,
                null,
                "zip",
                mxClamped,
                mmt,
                "Built-in ZIP deflate level 6 (single-thread)"),
        };
    }

    public static string NormalizePreset(string? preset)
    {
        var p = (preset ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            PresetStore => PresetStore,
            PresetDeflateFast => PresetDeflateFast,
            PresetDeflateBalanced => PresetDeflateBalanced,
            PresetDeflateMax => PresetDeflateMax,
            PresetSevenZip => PresetSevenZip,
            _ => PresetDeflateBalanced,
        };
    }

    public static string Normalize7zFormat(string? fmt)
    {
        var f = (fmt ?? "7z").Trim().ToLowerInvariant();
        return f is "zip" or "7z" ? f : "7z";
    }

    private static CompressionOptions BuildSevenZipOptions(string? sevenExe, string zfmt, int mx, int mmt)
    {
        var mmtDesc = mmt <= 0 ? "auto threads" : $"{mmt} threads";
        var summary = zfmt == "7z"
            ? $"7-Zip .7z LZMA2 -mx={mx} -mmt={mmtDesc}"
            : $"7-Zip .zip Deflate -mx={mx} -mmt={mmtDesc} (MT mainly when there are many files)";
        return new CompressionOptions(
            "7z",
            CompressionKind.Deflated,
            6,
            sevenExe,
            zfmt,
            mx,
            mmt,
            summary);
    }
}
