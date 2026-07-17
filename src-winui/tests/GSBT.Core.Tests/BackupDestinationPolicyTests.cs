using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupDestinationPolicyTests
{
    [Fact]
    public void HasPersistedDefault_requires_key_and_non_empty_value()
    {
        Assert.False(BackupDestinationPolicy.HasPersistedDefault(_ => false, (_, _) => string.Empty));
        Assert.False(BackupDestinationPolicy.HasPersistedDefault(_ => true, (_, _) => string.Empty));
        Assert.True(BackupDestinationPolicy.HasPersistedDefault(_ => true, (_, _) => @"C:\Backups"));
    }

    [Fact]
    public void GetSuggestion_prefers_default_over_last()
    {
        string Get(string key, string fallback) => key switch
        {
            "default_backup_path" => @"D:\Default",
            "last_backup_path" => @"D:\Last",
            _ => fallback,
        };

        Assert.Equal(@"D:\Default", BackupDestinationPolicy.GetSuggestion(Get));
    }

    [Fact]
    public void TryResolveNonInteractive_uses_explicit_path_first()
    {
        var ok = BackupDestinationPolicy.TryResolveNonInteractive(
            @"C:\Explicit",
            acceptSuggestion: false,
            _ => false,
            (_, _) => string.Empty,
            out var resolved,
            out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(@"C:\Explicit"), resolved);
    }

    [Fact]
    public void TryResolveNonInteractive_fails_without_path_or_default()
    {
        var ok = BackupDestinationPolicy.TryResolveNonInteractive(
            null,
            acceptSuggestion: false,
            _ => false,
            (_, _) => string.Empty,
            out _,
            out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void GetSuggestion_falls_back_to_documents_gsbt_backups()
    {
        var suggestion = BackupDestinationPolicy.GetSuggestion((_, fallback) => fallback);

        Assert.EndsWith(Path.Combine("Documents", "gsbt-backups"), suggestion);
    }

}
