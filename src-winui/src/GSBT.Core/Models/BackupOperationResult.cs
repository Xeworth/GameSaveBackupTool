namespace GSBT.Core.Models;

public enum BackupOperationStatus
{
    Succeeded,
    Partial,
    Failed,
    Cancelled,
}

/// <summary>Structured outcome shared by GUI, CLI, auto-backup, and restore safety snapshots.</summary>
public sealed record BackupOperationResult
{
    public required string GameName { get; init; }

    public required BackupOperationStatus Status { get; init; }

    public string Source { get; init; } = string.Empty;

    public string BackupPath { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public bool IsRegistry { get; init; }

    public int FilesCopied { get; init; }

    public long BytesCopied { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool Success => Status == BackupOperationStatus.Succeeded;

    public bool IsComplete => Success && string.IsNullOrWhiteSpace(Error);
}
