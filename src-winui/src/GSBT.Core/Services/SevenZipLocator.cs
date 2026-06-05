namespace GSBT.Core.Services;

/// <summary>Locate <c>7z.exe</c> on Windows (trusted install dirs only), matching Python <c>find_7zip_executable</c>.</summary>
public static class SevenZipLocator
{
    /// <summary>First trusted <c>7z.exe</c> found under Program Files, or null.</summary>
    public static string? FindSevenZipExecutable()
    {
        foreach (var cand in EnumerateTrustedInstallCandidates())
        {
            if (File.Exists(cand))
            {
                return cand;
            }
        }

        foreach (var name in new[] { "7z.exe", "7z" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath is not null && IsTrustedSevenZipPath(fromPath))
            {
                return fromPath;
            }
        }

        return null;
    }

    /// <summary>Custom path from settings when trusted; otherwise <see cref="FindSevenZipExecutable"/>.</summary>
    public static string? ResolveSevenZipExe(string? compression7zPathFromSettings)
    {
        var custom = (compression7zPathFromSettings ?? string.Empty).Trim().Trim('"');
        if (custom.Length > 0)
        {
            if (!IsTrustedSevenZipPath(custom))
            {
                return null;
            }

            return custom;
        }

        return FindSevenZipExecutable();
    }

    internal static bool IsTrustedSevenZipPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(path), "7z.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var full = Path.GetFullPath(path);
            foreach (var cand in EnumerateTrustedInstallCandidates())
            {
                if (string.Equals(full, cand, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateTrustedInstallCandidates()
    {
        foreach (var envKey in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var baseDir = Environment.GetEnvironmentVariable(envKey);
            if (string.IsNullOrEmpty(baseDir))
            {
                continue;
            }

            yield return Path.GetFullPath(Path.Combine(baseDir, "7-Zip", "7z.exe"));
        }

        yield return Path.GetFullPath(@"C:\Program Files\7-Zip\7z.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // ignore bad PATH segments
            }
        }

        return null;
    }
}
