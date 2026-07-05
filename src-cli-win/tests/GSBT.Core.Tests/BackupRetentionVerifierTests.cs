using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupRetentionVerifierTests
{
    [Fact]
    public void HasRetentionArtifact_false_when_only_empty_backup_run_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_ret_test_" + Guid.NewGuid().ToString("N"));
        var game = "Test Game Alpha";
        var safe = "Test Game Alpha"; // SanitizeForWindowsPathSegment may change — use simple name
        try
        {
            var baseDir = Path.Combine(root, safe);
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(Path.Combine(baseDir, $"{safe} - Backup 2099-01-01_at_00-00-00"));

            Assert.False(BackupRetentionVerifier.HasRetentionArtifact(root, game, subfolderPerGame: true));
            Assert.Null(BackupRetentionVerifier.TryInferLatestLastBackupIso(root, game, subfolderPerGame: true));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void HasRetentionArtifact_true_when_backup_run_contains_a_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_ret_test_" + Guid.NewGuid().ToString("N"));
        var game = "Test Game Beta";
        var safe = "Test Game Beta";
        try
        {
            var baseDir = Path.Combine(root, safe);
            var run = Path.Combine(baseDir, $"{safe} - Backup 2099-01-02_at_00-00-00");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, "save.dat"), "x");

            Assert.True(BackupRetentionVerifier.HasRetentionArtifact(root, game, subfolderPerGame: true));
            var iso = BackupRetentionVerifier.TryInferLatestLastBackupIso(root, game, subfolderPerGame: true);
            Assert.False(string.IsNullOrWhiteSpace(iso));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void ComputeTotalRetentionBackupBytes_sums_all_non_empty_runs()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_size_test_" + Guid.NewGuid().ToString("N"));
        var game = "Size Game";
        var safe = "Size Game";
        try
        {
            var baseDir = Path.Combine(root, safe);
            var run1 = Path.Combine(baseDir, $"{safe} - Backup 2099-01-01_at_00-00-00");
            var run2 = Path.Combine(baseDir, $"{safe} - Backup 2099-01-02_at_00-00-00");
            Directory.CreateDirectory(run1);
            Directory.CreateDirectory(run2);
            File.WriteAllText(Path.Combine(run1, "a.dat"), new string('x', 100));
            File.WriteAllText(Path.Combine(run2, "b.dat"), new string('y', 50));

            Assert.Equal(150, BackupRetentionVerifier.ComputeTotalRetentionBackupBytes(root, game, subfolderPerGame: true));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void HasRetentionArtifact_true_for_legacy_flat_reg_export()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_ret_legacy_" + Guid.NewGuid().ToString("N"));
        var game = "Registry Legacy Game";
        var safe = "Registry Legacy Game";
        try
        {
            var baseDir = Path.Combine(root, safe);
            Directory.CreateDirectory(baseDir);
            File.WriteAllText(Path.Combine(baseDir, $"{safe} - Backup 2099-01-03_at_00-00-00.reg"), "Windows Registry Editor Version 5.00");

            Assert.True(BackupRetentionVerifier.HasRetentionArtifact(root, game, subfolderPerGame: true));
            Assert.False(string.IsNullOrWhiteSpace(
                BackupRetentionVerifier.TryInferLatestLastBackupIso(root, game, subfolderPerGame: true)));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void RetentionRunContainsAtLeastOneFile_nested_file_counts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gsbt_nested_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "deep"));
            File.WriteAllText(Path.Combine(dir, "deep", "f.txt"), "1");
            Assert.True(BackupRetentionVerifier.RetentionRunContainsAtLeastOneFile(dir));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
