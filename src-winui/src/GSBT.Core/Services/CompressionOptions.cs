namespace GSBT.Core.Services;

/// <summary>Resolved compression options (mirrors Python <c>CompressionOptions</c>).</summary>
public sealed record CompressionOptions(
    string Engine,
    CompressionKind ZipKind,
    int DeflateLevel,
    string? SevenZipExe,
    string SevenArchiveFormat,
    int SevenMx,
    int SevenMmt,
    string SummaryLabel);

public enum CompressionKind
{
    Stored,
    Deflated
}
