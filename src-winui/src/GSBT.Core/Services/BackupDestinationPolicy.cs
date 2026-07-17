namespace GSBT.Core.Services;

/// <summary>Backup folder resolution rules shared by CLI and tests (no UI).</summary>
public static class BackupDestinationPolicy
{
    public static bool HasPersistedDefault(
        Func<string, bool> containsKey,
        Func<string, string, string> getString)
    {
        return containsKey("default_backup_path")
            && !string.IsNullOrWhiteSpace(getString("default_backup_path", string.Empty));
    }

    public static string? GetSuggestion(Func<string, string, string> getString)
    {
        var def = getString("default_backup_path", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(def))
        {
            return def;
        }

        var last = getString("last_backup_path", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(last)
            ? GSBT.Core.Common.BackupPaths.SuggestedDefaultBackupPath()
            : last;
    }

    /// <summary>Validates and normalizes a backup folder path.</summary>
    public static bool TryNormalizePath(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Enter a backup folder path.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(raw.Trim());
            Directory.CreateDirectory(normalized);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Non-interactive resolution: explicit path wins, then persisted default, then suggestion when acceptSuggestion is true.</summary>
    public static bool TryResolveNonInteractive(
        string? explicitPath,
        bool acceptSuggestion,
        Func<string, bool> containsKey,
        Func<string, string, string> getString,
        out string resolved,
        out string? error)
    {
        resolved = string.Empty;
        error = null;

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return TryNormalizePath(explicitPath, out resolved, out error);
        }

        if (HasPersistedDefault(containsKey, getString))
        {
            return TryNormalizePath(getString("default_backup_path", string.Empty), out resolved, out error);
        }

        if (acceptSuggestion)
        {
            var suggestion = GetSuggestion(getString);
            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                return TryNormalizePath(suggestion, out resolved, out error);
            }
        }

        error = "No backup destination configured. Use --path, gsbt settings backup-path, or run interactively.";
        return false;
    }
}
