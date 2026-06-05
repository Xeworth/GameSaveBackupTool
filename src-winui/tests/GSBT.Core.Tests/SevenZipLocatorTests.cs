using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class SevenZipLocatorTests
{
    [Fact]
    public void IsTrustedSevenZipPath_rejects_non_7z_name()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gsbt_7z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var fake = Path.Combine(temp, "not7zip.exe");
        File.WriteAllText(fake, "");
        try
        {
            Assert.False(SevenZipLocator.IsTrustedSevenZipPath(fake));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void IsTrustedSevenZipPath_rejects_7z_in_temp_directory()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gsbt_7z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var fake = Path.Combine(temp, "7z.exe");
        File.WriteAllText(fake, "");
        try
        {
            Assert.False(SevenZipLocator.IsTrustedSevenZipPath(fake));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void FindSevenZipExecutable_does_not_use_untrusted_path_only_install()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gsbt_7z_path_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var fake = Path.Combine(temp, "7z.exe");
        File.WriteAllText(fake, "");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", temp);
            var found = SevenZipLocator.FindSevenZipExecutable();
            if (found is not null)
            {
                Assert.True(SevenZipLocator.IsTrustedSevenZipPath(found));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
