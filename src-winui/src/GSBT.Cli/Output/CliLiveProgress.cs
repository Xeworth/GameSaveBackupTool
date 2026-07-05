namespace GSBT.Cli.Output;

public enum CliBranchStatus
{
    Success,
    Warning,
    Error,
}

public sealed record CliBranchEntry(string GameName, CliBranchStatus Status, string Label);

/// <summary>Single live progress line during work; colored branch tree printed once at completion.</summary>
public sealed class CliLiveProgress : IDisposable
{
    /// <summary>UTF-8 bullet; falls back cleanly in legacy consoles.</summary>
    public const string Bullet = "\u25CF";

    private readonly bool _enabled;
    private readonly int _barWidth;
    private string _lastLine = string.Empty;
    private bool _disposed;

    public CliLiveProgress(bool enabled, int barWidth = 20)
    {
        _enabled = enabled && !Console.IsOutputRedirected;
        _barWidth = barWidth;
    }

    public static string AsciiBar(int current, int total, int width = 20)
    {
        if (total <= 0)
        {
            return $"{new string('-', width)}   0%";
        }

        var pct = Math.Clamp((double)current / total, 0, 1);
        var full = (int)Math.Round(pct * width);
        return $"{new string('#', full)}{new string('-', width - full)} {(int)(pct * 100),3}%";
    }

    public static string ProgressLine(int current, int total, string tail, int barWidth = 20)
    {
        var bar = AsciiBar(current, total, barWidth);
        return $"[{bar}] ({current}/{total}) {tail}";
    }

    /// <summary>Update the single bottom progress line in place.</summary>
    public void SetCounter(int current, int total, string tail)
    {
        if (!_enabled)
        {
            return;
        }

        WriteLive($"{Bullet} {ProgressLine(current, total, tail, _barWidth)}");
    }

    /// <summary>Update the single bottom progress line using a 0–100 percent scale.</summary>
    public void SetPercent(int percent, string tail)
    {
        if (!_enabled)
        {
            return;
        }

        var bar = AsciiBar(percent, 100, _barWidth);
        WriteLive($"{Bullet} [{bar}] {tail}");
    }

    /// <summary>Erase the in-place live line without leaving it on screen.</summary>
    public void ClearLiveLine()
    {
        if (!_enabled || string.IsNullOrEmpty(_lastLine))
        {
            return;
        }

        var width = Math.Max(_lastLine.Length, Console.WindowWidth - 1);
        Console.Error.Write('\r' + new string(' ', width) + '\r');
        _lastLine = string.Empty;
    }

    /// <summary>Erase live line, print branch tree, then one final progress summary line.</summary>
    public void CompleteWithBranch(
        IReadOnlyList<CliBranchEntry> entries,
        int current,
        int total,
        string tail)
    {
        if (!_enabled)
        {
            return;
        }

        ClearLiveLine();

        for (var i = 0; i < entries.Count; i++)
        {
            WriteBranchLine(i, entries[i]);
        }

        Console.Error.WriteLine($"{Bullet} {ProgressLine(current, total, tail, _barWidth)}");
    }

    public void FinishLine()
    {
        if (!_enabled || string.IsNullOrEmpty(_lastLine))
        {
            return;
        }

        Console.Error.WriteLine();
        _lastLine = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClearLiveLine();
        _disposed = true;
    }

    private const string LightHorizontal = "\u2500"; // ─
    private const string LightTee = "\u251C";        // ├
    private const string TopCorner = "\u250C";       // ┌

    private static string FormatBranchPrefix(int index) =>
        index == 0
            ? $"{TopCorner}{LightHorizontal} "
            : $"{LightTee}{LightHorizontal} ";

    private static void WriteBranchLine(int index, CliBranchEntry entry)
    {
        var (fg, _) = entry.Status switch
        {
            CliBranchStatus.Success => ("\x1b[32m", "done"),
            CliBranchStatus.Warning => ("\x1b[33m", "warn"),
            _ => ("\x1b[31m", "err"),
        };
        var reset = "\x1b[0m";
        Console.Error.WriteLine($"{FormatBranchPrefix(index)}{entry.GameName} ({fg}{entry.Label}{reset})");
    }

    private void WriteLive(string line)
    {
        _lastLine = line;
        var width = Math.Max(40, Console.WindowWidth - 1);
        var text = line.Length < width ? line + new string(' ', width - line.Length) : line;
        Console.Error.Write($"\r{text}");
    }
}
