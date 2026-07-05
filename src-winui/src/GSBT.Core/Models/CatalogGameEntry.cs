namespace GSBT.Core.Models;

/// <summary>Headless catalog row for CLI and other non-WinUI hosts.</summary>
public sealed class CatalogGameEntry
{
    public required int ListIndex { get; init; }

    public required string GameName { get; init; }

    public required string Platform { get; init; }

    public bool IsUserAdded { get; init; }

    public string? SavePathRaw { get; init; }

    public string? SavePathResolved { get; init; }

    public bool SaveInRegistryOnly { get; init; }

    public string? SaveRegistryHive { get; init; }

    public string? SaveRegistrySubkey { get; init; }

    public bool HasSaveLocation { get; init; }

    public required string SaveStatusLabel { get; init; }

    public bool IsBackupable { get; init; }

    public bool IsCompressible { get; init; }

    public string? LastBackupIso { get; init; }

    public required string LastBackupDisplay { get; init; }

    public string? BackupSkipReason { get; init; }

    public string? CompressSkipReason { get; init; }

    public long? SaveSizeBytes { get; init; }

    public string? SaveSizeDisplay { get; init; }
}
