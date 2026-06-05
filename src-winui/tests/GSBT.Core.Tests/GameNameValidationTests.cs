using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class GameNameValidationTests
{
    [Fact]
    public void Sanitize_keeps_hash_and_typical_title_chars()
    {
        Assert.Equal("C#", GameNameInputValidation.SanitizeForWindowsPathSegment("C#"));
        Assert.Equal("Game Name (2024)", GameNameInputValidation.SanitizeForWindowsPathSegment("Game Name (2024)"));
    }

    [Fact]
    public void Sanitize_strips_invalid_filename_chars()
    {
        Assert.Equal("AB", GameNameInputValidation.SanitizeForWindowsPathSegment("A:B"));
        Assert.Equal("xy", GameNameInputValidation.SanitizeForWindowsPathSegment("x<y"));
    }

    [Fact]
    public void Validation_rejects_invalid_chars()
    {
        Assert.False(GameNameInputValidation.IsValidGameNameForStorage("bad:name", out _));
        Assert.True(GameNameInputValidation.IsValidGameNameForStorage("Good Name", out var err) && err is null);
    }

    [Fact]
    public void Backup_service_sanitize_matches_shared_rules()
    {
        var svc = new SaveFolderBackupService();
        Assert.Equal("C#", svc.SanitizeGameFolderName("C#"));
    }

    [Fact]
    public void Classify_bytes_matches_thresholds()
    {
        Assert.Equal(BackupSizeSeverity.Normal, BackupFolderSizeEstimator.Classify(1024));
        Assert.Equal(BackupSizeSeverity.Large, BackupFolderSizeEstimator.Classify(BackupFolderSizeEstimator.LargeSaveThresholdBytes));
        Assert.Equal(BackupSizeSeverity.Suspicious, BackupFolderSizeEstimator.Classify(BackupFolderSizeEstimator.SuspiciousSaveThresholdBytes));
    }

    [Fact]
    public void Collision_message_when_sanitized_names_match()
    {
        var messages = GameNameInputValidation.GetSanitizedFolderCollisionMessages(["A:B", "A*B", "Other"]);
        Assert.Single(messages);
        Assert.Contains("A:B", messages[0], StringComparison.Ordinal);
        Assert.Contains("A*B", messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Collision_messages_empty_when_names_distinct()
    {
        var messages = GameNameInputValidation.GetSanitizedFolderCollisionMessages(["Alpha", "Beta"]);
        Assert.Empty(messages);
    }
}
