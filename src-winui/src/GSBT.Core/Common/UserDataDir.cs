namespace GSBT.Core.Common;

public static class UserDataDir
{
    public static string GetAppUserDataDir(string appName = "GSBT", params string[] legacyNames)
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
