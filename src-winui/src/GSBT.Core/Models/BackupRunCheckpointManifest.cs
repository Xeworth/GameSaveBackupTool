namespace GSBT.Core.Models;

/// <summary>Serialized checkpoint for a single retention backup run (stored under AppData, not inside the backup tree).</summary>
public sealed class BackupRunCheckpointManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>GSBT assembly version string when the checkpoint was written.</summary>
    public string? WriterVersion { get; set; }

    public string GameName { get; set; } = string.Empty;

    /// <summary>Full normalized path to the backup run directory at capture time.</summary>
    public string BackupRunDirectory { get; set; } = string.Empty;

    public string SourceSaveDirectory { get; set; } = string.Empty;

    /// <summary>UTC ISO 8601 when the manifest was finalized after copy.</summary>
    public string CheckpointCapturedAtUtc { get; set; } = string.Empty;

    public List<BackupRunCheckpointFileEntry> Files { get; set; } = [];
}

public sealed class BackupRunCheckpointFileEntry
{
    /// <summary>Path relative to the backup run root using OS directory separators.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Includes leading dot (e.g. ".sav").</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary><see cref="FileAttributes"/> as stored string (e.g. Archive, NotContentIndexed).</summary>
    public string FileAttributes { get; set; } = string.Empty;

    public string CreatedTimeUtc { get; set; } = string.Empty;

    public string LastWriteTimeUtc { get; set; } = string.Empty;
}
