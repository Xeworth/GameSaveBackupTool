namespace GSBT.Core.Services;

/// <summary>Canonical path and destination-capacity checks for backup and restore operations.</summary>
public static class BackupPathSafety
{
    public static bool TryValidateSourceAndDestination(
        string sourceDirectory,
        string backupRoot,
        out string normalizedSource,
        out string normalizedRoot,
        out string? error)
    {
        normalizedSource = string.Empty;
        normalizedRoot = string.Empty;
        error = null;

        try
        {
            normalizedSource = NormalizeDirectory(sourceDirectory);
            normalizedRoot = NormalizeDirectory(backupRoot);
        }
        catch (Exception ex)
        {
            error = $"Invalid backup path: {ex.Message}";
            return false;
        }

        if (!Directory.Exists(normalizedSource))
        {
            error = "Save folder does not exist.";
            return false;
        }

        if (PathsEqual(normalizedSource, normalizedRoot))
        {
            error = "The backup destination cannot be the save folder itself.";
            return false;
        }

        if (IsContainedBy(normalizedRoot, normalizedSource))
        {
            error = "The backup destination cannot be inside the save folder.";
            return false;
        }

        if (IsContainedBy(normalizedSource, normalizedRoot))
        {
            error = "The save folder cannot be inside the backup destination.";
            return false;
        }

        return true;
    }

    public static bool HasSufficientFreeSpace(string destination, long requiredBytes, out string? error)
    {
        error = null;
        if (requiredBytes <= 0)
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                error = "The backup destination drive is not available.";
                return false;
            }

            var margin = Math.Max(64L * 1024 * 1024, requiredBytes / 20);
            var requiredWithMargin = requiredBytes > long.MaxValue - margin
                ? long.MaxValue
                : requiredBytes + margin;
            if (drive.AvailableFreeSpace < requiredWithMargin)
            {
                error = $"Insufficient free space. Need approximately {FormatBytes(requiredWithMargin)}, but {FormatBytes(drive.AvailableFreeSpace)} is available.";
                return false;
            }
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    public static bool IsContainedBy(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(NormalizeDirectory(parent), NormalizeDirectory(candidate));
        return !relative.Equals(".", StringComparison.Ordinal)
            && relative.Length > 0
            && !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

    public static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var amount = (double)Math.Max(0, value);
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return $"{amount:0.##} {units[unit]}";
    }
}
