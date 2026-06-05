using System.Runtime.Versioning;
using GSBT.Core.Services;
using Microsoft.Win32;

namespace GSBT.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class RegistrySaveBackupServiceTests
{
    private const string TestSubkey = @"Software\GSBT_Test_RegistryBackup";

    [Fact]
    public void Fingerprint_changes_when_value_changes()
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestSubkey, writable: true);
        Assert.NotNull(key);
        key.SetValue("GSBT_TestValue", "alpha", RegistryValueKind.String);

        Assert.True(
            RegistrySaveBackupService.TryComputeSnapshotFingerprint(
                "HKEY_CURRENT_USER",
                TestSubkey,
                out var fp1));
        Assert.False(string.IsNullOrEmpty(fp1));

        key.SetValue("GSBT_TestValue", "beta", RegistryValueKind.String);

        Assert.True(
            RegistrySaveBackupService.TryComputeSnapshotFingerprint(
                "HKEY_CURRENT_USER",
                TestSubkey,
                out var fp2));
        Assert.NotEqual(fp1, fp2);

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestSubkey, throwOnMissingSubKey: false);
        }
        catch
        {
            // ignore cleanup
        }
    }

    [Fact]
    public void BackupToRetentionFile_uses_timestamped_subfolder()
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestSubkey, writable: true);
        Assert.NotNull(key);
        key.SetValue("GSBT_TestValue", "export", RegistryValueKind.String);

        var root = Path.Combine(Path.GetTempPath(), "gsbt_reg_bak_" + Guid.NewGuid().ToString("N"));
        var game = "GSBT Reg Export Test";
        try
        {
            var svc = new RegistrySaveBackupService();
            svc.BackupToRetentionFile(
                game,
                "HKEY_CURRENT_USER",
                TestSubkey,
                root,
                retentionCount: 3,
                subfolderPerGame: true,
                CancellationToken.None,
                out var regPath,
                out var err);

            Assert.Null(err);
            Assert.False(string.IsNullOrWhiteSpace(regPath));
            Assert.EndsWith(".reg", regPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(regPath));

            var runDir = Path.GetDirectoryName(regPath);
            Assert.NotNull(runDir);
            Assert.StartsWith($"{game} - Backup", Path.GetFileName(runDir), StringComparison.Ordinal);
            Assert.Equal(Path.Combine(root, game), Path.GetDirectoryName(runDir));

            Assert.True(BackupRunManifestStore.TryReadCheckpointCapturedAtUtc(runDir, out _));
            Assert.False(BackupRunManifestStore.HasManifestDrift(runDir));
            BackupRunManifestStore.DeleteManifestForBackupRun(runDir);
        }
        finally
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestSubkey, throwOnMissingSubKey: false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }
    }

    [Fact]
    public void TryGetTargetFromCatalogRow_requires_registry_only_flag()
    {
        var row = new Dictionary<string, object?>
        {
            ["save_in_registry_only"] = true,
            ["save_registry_hive"] = "HKEY_CURRENT_USER",
            ["save_registry_subkey"] = TestSubkey,
        };

        Assert.True(RegistrySaveBackupService.TryGetTargetFromCatalogRow(row, out var target));
        Assert.Equal("HKEY_CURRENT_USER", target.Hive);
        Assert.Equal(TestSubkey, target.Subkey);
    }
}
