namespace GSBT.Core.Models;

public enum BackupVerificationMode
{
    Fast,
    Full,
}

public sealed record BackupVerificationIssue(string RelativePath, string Kind, string Message);

public sealed record BackupVerificationResult
{
    public required string BackupPath { get; init; }

    public required BackupVerificationMode Mode { get; init; }

    public bool CheckpointFound { get; init; }

    public bool IsValid => CheckpointFound && Issues.Count == 0;

    public int ExpectedFiles { get; init; }

    public int CheckedFiles { get; init; }

    public IReadOnlyList<BackupVerificationIssue> Issues { get; init; } = [];
}
