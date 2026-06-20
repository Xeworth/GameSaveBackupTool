using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

/// <summary>Live labels for what the compressor is working on (top-level game folder under the backup root).</summary>
public sealed class CompressionActivityTracker
{
    private volatile string _currentGameFolder = string.Empty;

    public event Action<string>? GameFolderChanged;
    public event Action? TrackChanged;

    public string CurrentGameFolder => _currentGameFolder;

    public string PreviousGameFolder { get; private set; } = string.Empty;

    public string UpcomingGameFolder { get; private set; } = string.Empty;

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

    public void ApplyTrackUpdate(CompressionGameTrackUpdate update)
    {
        PreviousGameFolder = update.Previous ?? string.Empty;
        _currentGameFolder = update.Current ?? string.Empty;
        UpcomingGameFolder = update.Upcoming ?? string.Empty;
        TrackChanged?.Invoke();
        if (!string.IsNullOrEmpty(_currentGameFolder))
        {
            GameFolderChanged?.Invoke(_currentGameFolder);
        }
    }

    public void Clear()
    {
        _currentGameFolder = string.Empty;
        PreviousGameFolder = string.Empty;
        UpcomingGameFolder = string.Empty;
    }
}
