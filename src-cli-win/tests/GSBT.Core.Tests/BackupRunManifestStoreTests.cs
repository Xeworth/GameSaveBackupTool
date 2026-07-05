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
}
