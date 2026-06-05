namespace GSBT.WinUI.Services;

/// <summary>
/// Cross-thread append-only log for the sandbox monitor (mirrors Python <c>_sandbox_log</c> categories).
/// </summary>
public sealed class SandboxLogHub
{
    private readonly object _lock = new();
    private readonly List<(string Category, string Message, DateTime Utc)> _tail = [];
    private readonly SettingsStore _store;

    private const int MinTailCap = 500;
    private const int MaxTailCap = 20_000;

    public SandboxLogHub(SettingsStore store) => _store = store;

    public event Action<string, string>? LineAppended;
    public event Action? PreferencesChanged;
    public event Action? Cleared;

    public void NotifyPreferencesChanged()
    {
        lock (_lock)
        {
            _tail.RemoveAll(e => !ShouldEmit(e.Category, MessageBodyFromStoredLine(e.Message)));
        }

        PreferencesChanged?.Invoke();
    }

    private static string MessageBodyFromStoredLine(string storedLine)
    {
        var idx = storedLine.IndexOf(" | ", StringComparison.Ordinal);
        return idx >= 0 && idx + 3 < storedLine.Length ? storedLine[(idx + 3)..] : storedLine;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _tail.Clear();
        }

        Cleared?.Invoke();
    }

    public void Log(string category, string message)
    {
        if (!ShouldEmit(category, message))
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss} | {message}";
        lock (_lock)
        {
            _tail.Add((category, line, DateTime.UtcNow));
            var cap = Math.Clamp(_store.Get("sandbox_log_max_tail_lines", 5000), MinTailCap, MaxTailCap);
            while (_tail.Count > cap)
            {
                _tail.RemoveAt(0);
            }
        }

        LineAppended?.Invoke(category, line);
    }

    public IReadOnlyList<(string Category, string Line)> Snapshot()
    {
        lock (_lock)
        {
            return _tail.Select(x => (x.Category, x.Message)).ToList();
        }
    }

    private bool ShouldEmit(string category, string message)
    {
        var detail = (_store.Get("sandbox_log_detail", "normal") ?? "normal").Trim().ToLowerInvariant();
        if (detail == "verbose")
        {
            return true;
        }

        if (detail == "quiet")
        {
            if (string.Equals(category, "warn", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(category, "benchmark", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(category, "compress", StringComparison.OrdinalIgnoreCase)
                && !IsCompressProgressTick(message))
            {
                return true;
            }

            if (string.Equals(category, "info", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("ready", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        if (!IsCategoryEnabled(category))
        {
            return false;
        }

        if (string.Equals(category, "compress", StringComparison.OrdinalIgnoreCase)
            && !_store.Get("sandbox_log_show_compress_ticks", true)
            && IsCompressProgressTick(message))
        {
            return false;
        }

        return true;
    }

    private bool IsCategoryEnabled(string category)
    {
        var key = CategoryToPreferenceKey(category);
        return _store.Get(key, true);
    }

    private static string CategoryToPreferenceKey(string category)
    {
        var c = (category ?? string.Empty).Trim().ToLowerInvariant();
        return c switch
        {
            "scan" => "sandbox_log_show_scan",
            "compress" => "sandbox_log_show_compress",
            "benchmark" => "sandbox_log_show_benchmark",
            "warn" => "sandbox_log_show_warn",
            "7zip" => "sandbox_log_show_7zip",
            _ => "sandbox_log_show_info",
        };
    }

    private static bool IsCompressProgressTick(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        if (!message.Contains('~', StringComparison.Ordinal) || !message.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        return message.Contains("7-Zip", StringComparison.Ordinal)
            || message.Contains("ZIP", StringComparison.Ordinal)
            || message.Contains("zip", StringComparison.Ordinal);
    }
}
