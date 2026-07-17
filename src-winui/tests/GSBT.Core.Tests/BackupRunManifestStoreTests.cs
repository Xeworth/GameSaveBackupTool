using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupRunManifestStoreTests
{
    [Fact]
    public void TryWriteManifest_roundtrip_and_HasManifestDrift_false_when_intact()
    {
        var run = Path.Combine(Path.GetTempPath(), "gsbt_cp_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(run, "slot1"));
            File.WriteAllText(Path.Combine(run, "slot1", "a.sav"), "hello");
            File.WriteAllText(Path.Combine(run, "notes.txt"), "x");

            BackupRunManifestStore.TryWriteManifest("My Game", @"C:\fake\save", run);

            Assert.True(BackupRunManifestStore.TryReadCheckpointCapturedAtUtc(run, out var captured));
            Assert.True(captured <= DateTime.UtcNow.AddMinutes(1));

            Assert.False(BackupRunManifestStore.HasManifestDrift(run));

            File.Delete(Path.Combine(run, "slot1", "a.sav"));
            Assert.True(BackupRunManifestStore.HasManifestDrift(run));
        }
        finally
        {
            try
            {
                Directory.Delete(run, recursive: true);
            }
            catch
            {
                // ignore
            }

            BackupRunManifestStore.DeleteManifestForBackupRun(run);
        }
    }

    [Fact]
    public void HasManifestDrift_false_when_no_checkpoint_file()
    {
        var run = Path.Combine(Path.GetTempPath(), "gsbt_cp2_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, "f.txt"), "1");
            Assert.False(BackupRunManifestStore.HasManifestDrift(run));
        }
        finally
        {
            try
            {
                Directory.Delete(run, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void Full_verification_detects_same_size_content_change_and_fast_detects_extra_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_cp_full_" + Guid.NewGuid().ToString("N"));
        var run = Path.Combine(root, "run");
        using var checkpointScope = BackupRunManifestStore.UseCheckpointsRootForTests(Path.Combine(root, "checkpoints"));
        try
        {
            Directory.CreateDirectory(run);
            var save = Path.Combine(run, "save.dat");
            File.WriteAllText(save, "AAAA");
            Assert.True(BackupRunManifestStore.TryWriteManifest("Hash Game", @"C:\fake\save", run));

            File.WriteAllText(save, "BBBB");
            Assert.True(BackupRunManifestStore.Verify(run, GSBT.Core.Models.BackupVerificationMode.Fast).IsValid);
            Assert.False(BackupRunManifestStore.Verify(run, GSBT.Core.Models.BackupVerificationMode.Full).IsValid);

            File.WriteAllText(Path.Combine(run, "extra.dat"), "x");
            var fast = BackupRunManifestStore.Verify(run, GSBT.Core.Models.BackupVerificationMode.Fast);
            Assert.Contains(fast.Issues, issue => issue.Kind == "extra");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
