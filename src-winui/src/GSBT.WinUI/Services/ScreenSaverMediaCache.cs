using System.Security.Cryptography;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

/// <summary>
/// Resolves screen saver media paths. Release builds ship a single <c>data/screensaver.7z</c>;
/// media is extracted once to local app data. Debug builds may still use loose files beside the exe.
/// </summary>
internal static class ScreenSaverMediaCache
{
    private static readonly object Gate = new();
    private static string? _cacheRoot;

    public static string ResolveVideoPath(string fileName) =>
        Path.Combine(GetMediaRoot(), "video", fileName);

    public static string ResolveAudioPath(string fileName) =>
        Path.Combine(GetMediaRoot(), "audio", fileName);

    public static bool VideoExists(string fileName) => File.Exists(ResolveVideoPath(fileName));

    public static bool AudioExists(string fileName) => File.Exists(ResolveAudioPath(fileName));

    public static void EnsureReady()
    {
        lock (Gate)
        {
            _ = GetMediaRoot();
        }
    }

    private static string GetMediaRoot()
    {
        if (_cacheRoot is not null)
        {
            return _cacheRoot;
        }

        var looseRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        if (HasLooseMedia(looseRoot))
        {
            _cacheRoot = looseRoot;
            return _cacheRoot;
        }

        var archivePath = ScreenSaverMediaArchive.ResolveBundledArchivePath(AppContext.BaseDirectory);
        if (!File.Exists(archivePath))
        {
            _cacheRoot = looseRoot;
            return _cacheRoot;
        }

        var cacheDir = Path.Combine(UserDataDir.GetWinUiUserDataDir(), "screensaver_cache");
        var stampPath = Path.Combine(cacheDir, ".archive.sha256");
        var archiveHash = ComputeFileSha256Hex(archivePath);

        if (!IsCacheValid(cacheDir, stampPath, archiveHash))
        {
            ScreenSaverMediaArchive.ExtractToDirectory(archivePath, cacheDir, cleanOutput: true);
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(stampPath, archiveHash);
        }

        _cacheRoot = cacheDir;
        return _cacheRoot;
    }

    private static bool HasLooseMedia(string assetsRoot)
    {
        var videoDir = Path.Combine(assetsRoot, "video");
        var audioDir = Path.Combine(assetsRoot, "audio");
        return Directory.Exists(videoDir)
            && Directory.Exists(audioDir)
            && Directory.EnumerateFiles(videoDir).Any()
            && Directory.EnumerateFiles(audioDir).Any();
    }

    private static bool IsCacheValid(string cacheDir, string stampPath, string archiveHash)
    {
        if (!Directory.Exists(cacheDir) || !File.Exists(stampPath))
        {
            return false;
        }

        if (!string.Equals(File.ReadAllText(stampPath).Trim(), archiveHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasLooseMedia(cacheDir);
    }

    private static string ComputeFileSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
