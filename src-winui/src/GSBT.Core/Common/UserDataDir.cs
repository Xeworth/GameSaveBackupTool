namespace GSBT.Core.Common;

/// <summary>
/// Per-user data directories. Roaming root is <c>%AppData%/Game Save Backup Tool</c>;
/// WinUI files live in <c>winui</c>. Short internal folders use lowercase <c>gsbt</c>.
/// </summary>
public static class UserDataDir
{
    public const string AppFolderName = "Game Save Backup Tool";

    /// <summary>Lowercase short subfolder for compact paths (sandbox sessions, etc.).</summary>
    public const string ShortSubdirName = "gsbt";

    public const string WinUiSubdir = "winui";

    /// <summary>Pre-v0.1.2 roaming folder name — migrated on first launch.</summary>
    public const string LegacyAppFolderName = "GSBT";

    private static readonly string[] RoamingLegacyFolderNames = ["GSBT", "GSBT_Lite", "GSBT_Light"];

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

    /// <summary>Return (and create) <c>%AppData%/Game Save Backup Tool</c>, migrating legacy folder names if present.</summary>
    public static string GetAppUserDataDir(string appName = AppFolderName, params string[] legacyNames)
    {
        legacyNames = legacyNames is { Length: > 0 } ? legacyNames : RoamingLegacyFolderNames;
        var baseDir = PlatformUserDataBase();
        var target = Path.Combine(baseDir, appName);
        Directory.CreateDirectory(target);

        foreach (var legacyName in legacyNames)
        {
            MigrateLegacyDirectory(Path.Combine(baseDir, legacyName), target);
        }

        return target;
    }

    /// <summary>Return (and create) <c>%AppData%/Game Save Backup Tool/winui</c> for WinUI user-generated files.</summary>
    public static string GetWinUiUserDataDir()
    {
        var root = GetAppUserDataDir();
        var target = Path.Combine(root, WinUiSubdir);
        Directory.CreateDirectory(target);
        MigrateWinUiFromLegacyAppRoot(root, target);
        MigrateWinUiFromLegacyRoamingGsbtRoot(target);
        return target;
    }

    /// <summary><c>%LocalAppData%/Game Save Backup Tool/gsbt</c> — short-path root for ephemeral WinUI data.</summary>
    public static string GetLocalShortDataDir()
    {
        var localBase = Environment.GetEnvironmentVariable("GSBT_LOCAL_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(localBase))
        {
            localBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        var target = Path.Combine(
            localBase,
            AppFolderName,
            ShortSubdirName);
        Directory.CreateDirectory(target);
        MigrateLegacyDirectory(
            Path.Combine(
                localBase,
                LegacyAppFolderName),
            Path.Combine(
                localBase,
                AppFolderName));
        MigrateLegacyDirectory(
            Path.Combine(
                localBase,
                LegacyAppFolderName,
                "SandboxSimulation"),
            Path.Combine(target, "sandbox"));
        return target;
    }

    /// <summary>Per-launch sandbox simulation session folders under the local short root.</summary>
    public static string GetSandboxSimulationSessionsRoot() =>
        Path.Combine(GetLocalShortDataDir(), "sandbox", "sessions");

    /// <summary>Absolute path to a file inside the WinUI user-data folder.</summary>
    public static string WinUiUserDataFile(string fileName) => Path.Combine(GetWinUiUserDataDir(), fileName);

    private static void MigrateWinUiFromLegacyAppRoot(string appRoot, string winUiDir)
    {
        foreach (var name in WinUiRootFilesToMigrate)
        {
            MigrateFileIfMissing(Path.Combine(appRoot, name), Path.Combine(winUiDir, name));
        }

        foreach (var dirName in WinUiRootDirectoriesToMigrate)
        {
            MigrateDirectoryIfMissing(Path.Combine(appRoot, dirName), Path.Combine(winUiDir, dirName));
        }
    }

    /// <summary>One-time: WinUI files may have lived under <c>%AppData%/GSBT/winui</c> before the app folder rename.</summary>
    private static void MigrateWinUiFromLegacyRoamingGsbtRoot(string winUiDir)
    {
        var legacyWinUi = Path.Combine(PlatformUserDataBase(), LegacyAppFolderName, WinUiSubdir);
        if (!Directory.Exists(legacyWinUi))
        {
            return;
        }

        foreach (var name in WinUiRootFilesToMigrate)
        {
            MigrateFileIfMissing(Path.Combine(legacyWinUi, name), Path.Combine(winUiDir, name));
        }

        foreach (var dirName in WinUiRootDirectoriesToMigrate)
        {
            MigrateDirectoryIfMissing(Path.Combine(legacyWinUi, dirName), Path.Combine(winUiDir, dirName));
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
        var overrideRoot = Environment.GetEnvironmentVariable("GSBT_USER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

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

    internal static void MigrateLegacyDirectoryForTests(string legacyDir, string newDir) =>
        MigrateLegacyDirectory(legacyDir, newDir);

    private static void MigrateLegacyDirectory(string legacyDir, string newDir)
    {
        if (!Directory.Exists(legacyDir))
        {
            return;
        }

        Directory.CreateDirectory(newDir);

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
