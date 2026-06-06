namespace GSBT.Core.Common;

/// <summary>
/// Per-user GSBT data directories. Shared root is <c>%AppData%/GSBT</c>; each app flavor uses its own subfolder
/// (e.g. <c>winui</c>, <c>pyqt</c>) so parallel installs do not collide.
/// </summary>
public static class UserDataDir
{
    public const string GsbtAppName = "GSBT";
    public const string WinUiSubdir = "winui";

    private static readonly string[] WinUiRootFilesToMigrate =
    [
        "game_save_data.json",
        "ludusavi-save-manifest.json",
        "ludusavi-save-manifest.meta.json",
        "winui_settings.json",
        "sandbox_compression_benchmarks.json",
    ];

    private static readonly string[] WinUiRootDirectoriesToMigrate =
    [
        "backup_run_checkpoints",
        "logs",
        "notifications",
    ];

    /// <summary>Return (and create) <c>%AppData%/GSBT</c>, migrating legacy folder names if present.</summary>
    public static string GetAppUserDataDir(string appName = GsbtAppName, params string[] legacyNames)
    {
        legacyNames = legacyNames is { Length: > 0 } ? legacyNames : ["GSBT_Lite", "GSBT_Light"];
        var baseDir = PlatformUserDataBase();
        var target = Path.Combine(baseDir, appName);
        Directory.CreateDirectory(target);

        foreach (var legacyName in legacyNames)
        {
            MigrateLegacyDirectory(Path.Combine(baseDir, legacyName), target);
        }

        return target;
    }

    /// <summary>Return (and create) <c>%AppData%/GSBT/winui</c> for WinUI user-generated files.</summary>
    public static string GetWinUiUserDataDir()
    {
        var root = GetAppUserDataDir();
        var target = Path.Combine(root, WinUiSubdir);
        Directory.CreateDirectory(target);
        MigrateWinUiFromLegacyGsbtRoot(root, target);
        return target;
    }

    /// <summary>Absolute path to a file inside the WinUI user-data folder.</summary>
    public static string WinUiUserDataFile(string fileName) => Path.Combine(GetWinUiUserDataDir(), fileName);

    private static void MigrateWinUiFromLegacyGsbtRoot(string gsbtRoot, string winUiDir)
    {
        foreach (var name in WinUiRootFilesToMigrate)
        {
            MigrateFileIfMissing(Path.Combine(gsbtRoot, name), Path.Combine(winUiDir, name));
        }

        foreach (var dirName in WinUiRootDirectoriesToMigrate)
        {
            MigrateDirectoryIfMissing(Path.Combine(gsbtRoot, dirName), Path.Combine(winUiDir, dirName));
        }
    }

    private static void MigrateFileIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void MigrateDirectoryIfMissing(string source, string destination)
    {
        if (!Directory.Exists(source) || Directory.Exists(destination))
        {
            return;
        }

        try
        {
            CopyDirectory(source, destination);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string PlatformUserDataBase()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return xdg;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static void MigrateLegacyDirectory(string legacyDir, string newDir)
    {
        if (!Directory.Exists(legacyDir))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(legacyDir))
        {
            var target = Path.Combine(newDir, Path.GetFileName(entry));
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry))
                {
                    CopyDirectory(entry, target);
                }
                else
                {
                    File.Copy(entry, target);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dst = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: false);
        }
    }
}
