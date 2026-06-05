namespace GSBT.Core.Models;

public sealed record SaveScanResult
{
    public required string RowId { get; init; }
    public required string Name { get; init; }
    public string? AppId { get; init; }
    public string? InstallPath { get; init; }
    public string Platform { get; init; } = "Unknown";
    public string? SavePathRaw { get; init; }
    public string? SavePathResolved { get; init; }
    public string? SaveLocationDisplay { get; init; }
    public bool SaveInRegistryOnly { get; init; }
    public string? SaveRegistryHive { get; init; }
    public string? SaveRegistrySubkey { get; init; }
    public string Source { get; init; } = "Not Found";
    public double WallSec { get; init; }
    public string ScanOutcome { get; init; } = "NO_MANIFEST_PATHS";
}
