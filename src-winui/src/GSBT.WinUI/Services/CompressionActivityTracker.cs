namespace GSBT.WinUI.Services;

/// <summary>Live label for what the compressor is working on (top-level game folder under the backup root).</summary>
public sealed class CompressionActivityTracker
{
    private volatile string _currentGameFolder = string.Empty;

    public event Action<string>? GameFolderChanged;

    public string CurrentGameFolder => _currentGameFolder;

    public void SetCurrentGameFolder(string? topLevelFolder)
    {
        var next = string.IsNullOrWhiteSpace(topLevelFolder)
            ? string.Empty
            : topLevelFolder.Trim();
        if (string.Equals(next, _currentGameFolder, StringComparison.Ordinal))
        {
            return;
        }

        _currentGameFolder = next;
        if (!string.IsNullOrEmpty(next))
        {
            GameFolderChanged?.Invoke(next);
        }
    }

    public void Clear() => _currentGameFolder = string.Empty;
}
