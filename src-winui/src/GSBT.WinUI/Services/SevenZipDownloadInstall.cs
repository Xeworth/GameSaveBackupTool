using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GSBT.WinUI.Services;

/// <summary>
/// Download pinned 7-Zip installer and run silent setup (Python <c>seven_zip_install</c> parity).
/// Log every major step via <paramref name="log"/> — use <see cref="SandboxLogHub"/> category <c>7zip</c> when testing with <c>-sandbox</c>.
/// </summary>
public static class SevenZipDownloadInstall
{
    public const string PinnedDisplayVersion = "23.01";
    public const string PinnedBuild = "7z2301";

    /// <summary>Official installers are ~1.6 MB; reject oversized downloads before hash check.</summary>
    public const int MaxInstallerDownloadBytes = 5 * 1024 * 1024;

    // SHA-256 of official 7-zip.org installers (7z2301, June 2023). Re-verify when pinning a new build.
    private const string Sha256X64 = "26CB6E9F56333682122FAFE79DBCDFD51E9F47CC7217DCCD29AC6FC33B5598CD";
    private const string Sha256Arm64 = "6FA4CB35CBEBB0A46B8BBC22D1686A340E183C1F875D8B714EFDC39AF93DEBDA";
    private const string Sha256X86 = "9B6682255BED2E415BFA2EF75E7E0888158D1AAF79370DEFAA2E2A5F2B003A59";

    public static (string Url, string FileName) PinnedInstallerUrlAndName()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch switch
        {
            Architecture.X64 => ($"https://www.7-zip.org/a/{PinnedBuild}-x64.exe", $"{PinnedBuild}-x64.exe"),
            Architecture.Arm64 => ($"https://www.7-zip.org/a/{PinnedBuild}-arm64.exe", $"{PinnedBuild}-arm64.exe"),
            _ => ($"https://www.7-zip.org/a/{PinnedBuild}.exe", $"{PinnedBuild}.exe"),
        };
    }

    public static string ConsentSummaryText()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip");
        return
            $"This will download 7-Zip {PinnedDisplayVersion} ({PinnedBuild}) from the official 7-zip.org site — a fixed version the app can rely on, which may be older than the newest release.\n\n" +
            $"The installer will run silently into the default folder (usually \"{dir}\"). Windows may show one User Account Control (UAC) prompt for administrator approval.\n\n" +
            "After installation, this app detects 7-Zip in Program Files even without PATH.\n\n" +
            "Requires an internet connection. Continue?";
    }

    public static async Task DownloadInstallerAsync(
        HttpClient http,
        string destPath,
        IProgress<(long done, long? total)>? progress,
        CancellationToken cancellationToken,
        Action<string>? log)
    {
        var (url, _) = PinnedInstallerUrlAndName();
        log?.Invoke($"7zip: GET {url}");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        if (total is > MaxInstallerDownloadBytes)
        {
            throw new InvalidOperationException(
                $"7-Zip installer download exceeds the size limit ({MaxInstallerDownloadBytes} bytes).");
        }

        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[256 * 1024];
        long done = 0;
        int read;
        try
        {
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                done += read;
                if (done > MaxInstallerDownloadBytes)
                {
                    throw new InvalidOperationException(
                        $"7-Zip installer download exceeds the size limit ({MaxInstallerDownloadBytes} bytes).");
                }

                await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                if (total is > 0)
                {
                    progress?.Report((done, total));
                }
            }

            if (total is > 0)
            {
                progress?.Report((total.Value, total));
            }

            log?.Invoke($"7zip: saved installer ({done} bytes) → {destPath}");
            VerifyInstallerSha256(destPath, log);
        }
        catch
        {
            TryDeletePartialDownload(destPath);
            throw;
        }
    }

    private static void TryDeletePartialDownload(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Throws if the downloaded file does not match the pinned official hash.</summary>
    public static void VerifyInstallerSha256(string filePath, Action<string>? log)
    {
        var expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => Sha256X64,
            Architecture.Arm64 => Sha256Arm64,
            _ => Sha256X86,
        };

        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // ignore
            }

            throw new InvalidOperationException(
                "Downloaded 7-Zip installer failed integrity check (hash mismatch). The file was not run.");
        }

        log?.Invoke("7zip: installer SHA-256 verified.");
    }

    /// <summary>Run official installer <c>/S /D=...</c>; may show UAC on second attempt.</summary>
    public static async Task<(int Code, string Mode)> InstallSilentAsync(string installerPath, string installDir, Action<string>? log)
    {
        installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        Directory.CreateDirectory(installDir);
        var args = $"/S /D=\"{installDir}\"";
        var code = await RunInstallerNormalAsync(installerPath, args, log).ConfigureAwait(false);
        if (code == 0)
        {
            return (0, "standard");
        }

        log?.Invoke($"7zip: normal install exit {code}; retrying elevated (UAC)…");
        code = await RunInstallerElevatedAsync(installerPath, args, log).ConfigureAwait(false);
        return (code, "elevated");
    }

    private const int ErrorElevationRequired = 740;

    private static async Task<int> RunInstallerNormalAsync(string installerPath, string arguments, Action<string>? log)
    {
        try
        {
            using var p = Process.Start(
                new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            if (p is null)
            {
                return -1;
            }

            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            log?.Invoke($"7zip: Win32 elevation required ({ErrorElevationRequired}).");
            return ErrorElevationRequired;
        }
        catch (Exception ex)
        {
            log?.Invoke($"7zip: start failed: {ex.Message}");
            throw;
        }
    }

    private static async Task<int> RunInstallerElevatedAsync(string installerPath, string arguments, Action<string>? log)
    {
        try
        {
            using var p = Process.Start(
                new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                });
            if (p is null)
            {
                return -1;
            }

            await p.WaitForExitAsync().ConfigureAwait(false);
            log?.Invoke($"7zip: elevated installer exit code {p.ExitCode}");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            log?.Invoke($"7zip: elevated start failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>Wait until <c>7z.exe</c> appears under Program Files\7-Zip (post-install race).</summary>
    public static async Task<string?> WaitForSevenZipExeAsync(TimeSpan maxWait, TimeSpan poll, Action<string>? log)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
        var deadline = DateTime.UtcNow + maxWait;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(exe))
            {
                log?.Invoke($"7zip: detected {exe}");
                return exe;
            }

            await Task.Delay(poll).ConfigureAwait(false);
        }

        log?.Invoke("7zip: timeout waiting for 7z.exe after install.");
        return null;
    }
}
