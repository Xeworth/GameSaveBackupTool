using System.Collections.Concurrent;

namespace GSBT.WinUI.Services;

/// <summary>Ensures at most one in-flight backup per game (manual + auto-backup share this gate).</summary>
public sealed class GameBackupCoordinator
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public bool TryBegin(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return false;
        }

        return _inFlight.TryAdd(gameName.Trim(), 0);
    }

    public void End(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return;
        }

        _inFlight.TryRemove(gameName.Trim(), out _);
    }
}
