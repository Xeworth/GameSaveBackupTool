using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class RestoreServiceTests
{
    [Fact]
    public void Replace_restore_swaps_verified_snapshot_and_keeps_pre_restore_safety_copy()
    {
        using var temp = new RestoreTestDirectory();
        var snapshot = temp.CreateDirectory("snapshot");
        var target = temp.CreateDirectory("live");
        var safetyRoot = temp.CreateDirectory("safety");
        File.WriteAllText(Path.Combine(snapshot, "save.dat"), "backup-version");
        File.WriteAllText(Path.Combine(target, "save.dat"), "live-version");
        Assert.True(BackupRunManifestStore.TryWriteManifest("Restore Game", target, snapshot));
        var service = new RestoreService();

        var plan = service.CreateFolderPlan("Restore Game", snapshot, target, RestoreMode.Replace);
        var result = service.ExecuteFolderRestore(plan, safetyRoot, TestContext.Current.CancellationToken);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.True(result.Success, result.Error);
        Assert.Equal("backup-version", File.ReadAllText(Path.Combine(target, "save.dat")));
        Assert.Equal("live-version", File.ReadAllText(Path.Combine(result.SafetySnapshotPath, "save.dat")));
    }

    [Fact]
    public void Merge_restore_preserves_unrelated_live_files()
    {
        using var temp = new RestoreTestDirectory();
        var snapshot = temp.CreateDirectory("snapshot");
        var target = temp.CreateDirectory("live");
        var safetyRoot = temp.CreateDirectory("safety");
        File.WriteAllText(Path.Combine(snapshot, "save.dat"), "restored");
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Assert.True(BackupRunManifestStore.TryWriteManifest("Merge Game", target, snapshot));
        var service = new RestoreService();

        var plan = service.CreateFolderPlan("Merge Game", snapshot, target, RestoreMode.Merge);
        var result = service.ExecuteFolderRestore(plan, safetyRoot, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal("restored", File.ReadAllText(Path.Combine(target, "save.dat")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void Modified_snapshot_is_rejected_before_live_files_change()
    {
        using var temp = new RestoreTestDirectory();
        var snapshot = temp.CreateDirectory("snapshot");
        var target = temp.CreateDirectory("live");
        File.WriteAllText(Path.Combine(snapshot, "save.dat"), "AAAA");
        File.WriteAllText(Path.Combine(target, "save.dat"), "live");
        Assert.True(BackupRunManifestStore.TryWriteManifest("Drift Game", target, snapshot));
        File.WriteAllText(Path.Combine(snapshot, "save.dat"), "BBBB");

        var plan = new RestoreService().CreateFolderPlan("Drift Game", snapshot, target, RestoreMode.Replace);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, error => error.Contains("hash", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("live", File.ReadAllText(Path.Combine(target, "save.dat")));
    }

    private sealed class RestoreTestDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gsbt_restore_test_" + Guid.NewGuid().ToString("N"));
        private readonly IDisposable _checkpointScope;

        public RestoreTestDirectory()
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
