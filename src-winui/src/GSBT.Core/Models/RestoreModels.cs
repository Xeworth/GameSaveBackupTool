namespace GSBT.Core.Models;

public enum RestoreMode
{
    Replace,
    Merge,
    Alternate,
}

public enum RestoreOperationStatus
{
    Succeeded,
    Failed,
    RolledBack,
    Partial,
    Cancelled,
}

public sealed record RestorePlan
{
    public required string GameName { get; init; }

    public required string BackupRunPath { get; init; }

    public required string TargetPath { get; init; }

    public required RestoreMode Mode { get; init; }

    public bool IsRegistry { get; init; }

    public bool IsValid { get; init; }

    public int FileCount { get; init; }

    public long TotalBytes { get; init; }

    public int ConflictCount { get; init; }

    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record RestoreOperationResult
{
    public required string GameName { get; init; }

    public required RestoreOperationStatus Status { get; init; }

    public string TargetPath { get; init; } = string.Empty;

    public string SafetySnapshotPath { get; init; } = string.Empty;

    public int FilesRestored { get; init; }

    public long BytesRestored { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool Success => Status == RestoreOperationStatus.Succeeded;
}
