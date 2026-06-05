namespace GSBT.Core.Common;

/// <summary>
/// Validates titles used as Windows folder/file name segments (backup folders, catalog keys on disk).
/// </summary>
public static class GameNameInputValidation
{
    /// <summary>Characters Windows does not allow in file or folder names (excluding control chars in display).</summary>
    public static string InvalidFileNameCharactersForUserMessage =>
        "\\ / : * ? \" < > |";

    public static bool ContainsInvalidFileNameCharacters(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        return s.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars().AsSpan()) >= 0;
    }

    /// <summary>Returns false when the string cannot be used as a single path segment after trimming.</summary>
    public static bool IsValidGameNameForStorage(string? s, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(s))
        {
            errorMessage = "Enter a game name.";
            return false;
        }

        if (ContainsInvalidFileNameCharacters(s))
        {
            errorMessage =
                $"A game name cannot include these characters: {InvalidFileNameCharactersForUserMessage}";
            return false;
        }

        // Windows forbids trailing spaces/dots in path components.
        if (s.AsSpan().TrimEnd().Length < s.Length || s.EndsWith(".", StringComparison.Ordinal))
        {
            errorMessage = "Remove trailing spaces or dots from the name.";
            return false;
        }

        return true;
    }

    /// <summary>Same rules as backup subfolder names: strip invalid filename chars and trailing space/dot.</summary>
    public static string SanitizeForWindowsPathSegment(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return "Game";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = gameName.Where(c => Array.IndexOf(invalid, c) < 0).ToArray();
        var t = new string(chars).TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(t) ? "Game" : t;
    }

    /// <summary>
    /// When two display names sanitize to the same backup folder prefix, retention pruning can overlap.
    /// </summary>
    public static string? TryGetSanitizedFolderCollisionMessage(string gameName, IEnumerable<string> otherGameNames)
    {
        var safe = SanitizeForWindowsPathSegment(gameName);
        foreach (var other in otherGameNames)
        {
            if (string.Equals(other, gameName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(SanitizeForWindowsPathSegment(other), safe, StringComparison.OrdinalIgnoreCase))
            {
                return $"“{gameName}” and “{other}” share the same backup folder name (“{safe}”). "
                    + "Rename one game to avoid retention deleting the wrong backups.";
            }
        }

        return null;
    }

    /// <summary>Distinct collision messages for every pair in <paramref name="gameNames"/> that share a sanitized folder.</summary>
    public static IReadOnlyList<string> GetSanitizedFolderCollisionMessages(IEnumerable<string> gameNames)
    {
        var names = gameNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var messages = new List<string>();
        var reportedSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var safe = SanitizeForWindowsPathSegment(name);
            if (!reportedSafe.Add(safe))
            {
                continue;
            }

            var msg = TryGetSanitizedFolderCollisionMessage(name, names);
            if (msg is not null)
            {
                messages.Add(msg);
            }
        }

        return messages;
    }
}
