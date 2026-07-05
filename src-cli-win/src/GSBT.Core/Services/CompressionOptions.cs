namespace GSBT.Core.Services;

/// <summary>Resolved native 7-Zip compression options (bundled <c>7z.dll</c>).</summary>
public sealed record CompressionOptions(
    int SevenMx,
    int SevenMmt,
    bool SolidArchive,
    string SummaryLabel)
{
    public const string EngineNative7z = "7z";

    public string Engine => EngineNative7z;
}
