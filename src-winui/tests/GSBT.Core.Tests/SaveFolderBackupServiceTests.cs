using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class SaveFolderBackupServiceTests
{
    [Fact]
    public void Successful_replacement_is_promoted_before_old_run_is_pruned()
    {
        using var temp = new BackupTestDirectory();
        var source = temp.CreateDirectory("source");
        var root = temp.CreateDirectory("backup");
        File.WriteAllText(Path.Combine(source, "save.dat"), "first");
        var service = new SaveFolderBackupService();

        var first = service.BackupToRetentionFolderWithResult("Test Game", source, root, 1, true, TestContext.Current.CancellationToken);
        Assert.True(first.Success, first.Error);

        File.WriteAllText(Path.Combine(source, "save.dat"), "second");
        var second = service.BackupToRetentionFolderWithResult("Test Game", source, root, 1, true, TestContext.Current.CancellationToken);

        Assert.True(second.Success, second.Error);
        Assert.False(Directory.Exists(first.BackupPath));
        Assert.True(Directory.Exists(second.BackupPath));
        Assert.Equal("second", File.ReadAllText(Path.Combine(second.BackupPath, "save.dat")));
    }

    [Fact]
    public void Failed_copy_preserves_previous_valid_backup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new BackupTestDirectory();
        var source = temp.CreateDirectory("source");
        var root = temp.CreateDirectory("backup");
        var save = Path.Combine(source, "save.dat");
        File.WriteAllText(save, "stable");
        var service = new SaveFolderBackupService();
        var first = service.BackupToRetentionFolderWithResult("Locked Game", source, root, 1, true, TestContext.Current.CancellationToken);
        Assert.True(first.Success, first.Error);

        using (File.Open(save, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = service.BackupToRetentionFolderWithResult("Locked Game", source, root, 1, true, TestContext.Current.CancellationToken);
            Assert.Equal(BackupOperationStatus.Failed, failed.Status);
        }

        Assert.True(Directory.Exists(first.BackupPath));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(Path.Combine(root, "Locked Game")),
            p => Path.GetFileName(p).StartsWith(".gsbt-staging-", StringComparison.Ordinal));
    }

    [Fact]
    public void Cancellation_does_not_create_or_prune_a_run()
    {
        using var temp = new BackupTestDirectory();
        var source = temp.CreateDirectory("source");
        var root = temp.CreateDirectory("backup");
        File.WriteAllText(Path.Combine(source, "save.dat"), "stable");
        var service = new SaveFolderBackupService();
        var first = service.BackupToRetentionFolderWithResult("Cancel Game", source, root, 1, true, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            service.BackupToRetentionFolderWithResult("Cancel Game", source, root, 1, true, cts.Token));
        Assert.True(Directory.Exists(first.BackupPath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Retention_keeps_exact_requested_number_of_verified_runs(int retention)
    {
        using var temp = new BackupTestDirectory();
        var source = temp.CreateDirectory("source");
        var root = temp.CreateDirectory("backup");
        var service = new SaveFolderBackupService();

        for (var i = 0; i < retention + 2; i++)
        {
            File.WriteAllText(Path.Combine(source, "save.dat"), i.ToString());
            var result = service.BackupToRetentionFolderWithResult("Retention Game", source, root, retention, true, TestContext.Current.CancellationToken);
            Assert.True(result.Success, result.Error);
        }

        var runs = Directory.EnumerateDirectories(Path.Combine(root, "Retention Game"))
            .Where(p => !Path.GetFileName(p).StartsWith(".gsbt-staging-", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(retention, runs.Count);
    }

    [Theory]
    [InlineData("root-equals-source")]
    [InlineData("root-inside-source")]
    [InlineData("source-inside-root")]
    public void Unsafe_source_destination_relationship_is_rejected(string scenario)
    {
        using var temp = new BackupTestDirectory();
        string source;
        string root;
        switch (scenario)
        {
            case "root-equals-source":
                source = temp.CreateDirectory("same");
                root = source;
                break;
            case "root-inside-source":
                source = temp.CreateDirectory("source");
                root = Path.Combine(source, "backup");
                break;
            default:
                root = temp.CreateDirectory("backup");
                source = Path.Combine(root, "source");
                Directory.CreateDirectory(source);
                break;
        }

        var result = new SaveFolderBackupService()
            .BackupToRetentionFolderWithResult("Unsafe Game", source, root, 3, true, TestContext.Current.CancellationToken);

        Assert.Equal(BackupOperationStatus.Failed, result.Status);
        Assert.Contains("cannot", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BackupTestDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gsbt_backup_test_" + Guid.NewGuid().ToString("N"));
        private readonly IDisposable _checkpointScope;

        public BackupTestDirectory()
        {
            Directory.CreateDirectory(_root);
            _checkpointScope = BackupRunManifestStore.UseCheckpointsRootForTests(Path.Combine(_root, "checkpoints"));
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            _checkpointScope.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
