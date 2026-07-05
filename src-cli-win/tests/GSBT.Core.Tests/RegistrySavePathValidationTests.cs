using System.Runtime.Versioning;
using GSBT.Core.Services;
using Microsoft.Win32;

namespace GSBT.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class RegistrySavePathValidationTests
{
    private const string TestSubkey = @"Software\GSBT_Test_Validation";

    [Theory]
    [InlineData(@"C:\Games\Save")]
    [InlineData(@"D:/SaveData")]
    [InlineData(@"\\server\share\saves")]
    public void LooksLikeFilesystemPath_detects_drive_and_unc(string path)
    {
        Assert.True(RegistrySaveResolver.LooksLikeFilesystemPath(path));
    }

    [Fact]
    public void LooksLikeFilesystemPath_does_not_flag_registry()
    {
        Assert.False(RegistrySaveResolver.LooksLikeFilesystemPath(@"HKCU\Software\GSBT_Test"));
    }

    [Fact]
    public void Validate_rejects_unknown_hive()
    {
        var resolver = new RegistrySaveResolver();
        var result = resolver.ValidateRegistrySaveHint(@"HKEY_PERFORMANCE_DATA\Foo");
        Assert.False(result.IsSuccess);
        Assert.Equal(RegistrySaveResolver.RegistrySaveValidationKind.UnknownHive, result.Kind);
    }

    [Fact]
    public void Validate_rejects_missing_subkey()
    {
        var resolver = new RegistrySaveResolver();
        var result = resolver.ValidateRegistrySaveHint("HKCU");
        Assert.False(result.IsSuccess);
        Assert.Equal(RegistrySaveResolver.RegistrySaveValidationKind.MissingSubkeyPath, result.Kind);
    }

    [Fact]
    public void Validate_accepts_in_key_save_with_values()
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestSubkey, writable: true);
        Assert.NotNull(key);
        key.SetValue("GSBT_TestValue", "data", RegistryValueKind.String);

        try
        {
            var resolver = new RegistrySaveResolver();
            var result = resolver.ValidateRegistrySaveHint($@"HKCU\{TestSubkey}");
            Assert.True(result.IsSuccess);
            Assert.Equal(RegistrySaveResolver.RegistrySaveValidationKind.ValidInKey, result.Kind);
            Assert.Equal("HKEY_CURRENT_USER", result.Hive);
            Assert.Equal(TestSubkey, result.Subkey);
        }
        finally
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestSubkey, throwOnMissingSubKey: false);
            }
            catch
            {
                // ignore cleanup
            }
        }
    }

    [Fact]
    public void Validate_rejects_missing_key()
    {
        var resolver = new RegistrySaveResolver();
        var result = resolver.ValidateRegistrySaveHint(@"HKCU\Software\GSBT_NoSuchKey_12345");
        Assert.False(result.IsSuccess);
        Assert.Equal(RegistrySaveResolver.RegistrySaveValidationKind.KeyNotFound, result.Kind);
    }
}
