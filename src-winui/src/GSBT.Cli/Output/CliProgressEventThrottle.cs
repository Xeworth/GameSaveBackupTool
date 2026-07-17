namespace GSBT.Cli.Output;

public readonly record struct CliProgressEmission(
    int Percent,
    bool IsHeartbeat,
    int ElapsedSeconds,
    int PlateauSeconds);

/// <summary>
/// Reduces high-frequency native progress callbacks to useful agent events while preserving
/// periodic heartbeats during long plateaus such as archive finalization at 99%.
/// </summary>
public sealed class CliProgressEventThrottle
{
    public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _minimumInterval;
    private readonly TimeSpan _heartbeatInterval;
    private readonly object _sync = new();
    private bool _started;
    private int _observedPercent = -1;
    private int _lastEmittedPercent = -1;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastObservedChangeAt;
    private DateTimeOffset _lastEmittedAt;

    public CliProgressEventThrottle(
        Func<DateTimeOffset>? clock = null,
        TimeSpan? minimumInterval = null,
        TimeSpan? heartbeatInterval = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _minimumInterval = minimumInterval ?? DefaultMinimumInterval;
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
    }

    public CliProgressEmission? Observe(int percent)
    {
        lock (_sync)
        {
            var now = _clock();
            percent = Math.Clamp(percent, 0, 100);
            if (!_started)
            {
                _started = true;
                _startedAt = now;
                _lastObservedChangeAt = now;
                _lastEmittedAt = now;
                _observedPercent = percent;
                _lastEmittedPercent = percent;
                return CreateEmission(percent, isHeartbeat: false, now);
            }

            // Compression progress is expected to be monotonic. Ignore late queued regressions.
            if (percent < _observedPercent)
            {
                percent = _observedPercent;
            }

            if (percent != _observedPercent)
            {
                _observedPercent = percent;
                _lastObservedChangeAt = now;
            }

            if (_observedPercent != _lastEmittedPercent)
            {
                var movedEnough = _observedPercent - _lastEmittedPercent >= 5;
                var waitedEnough = now - _lastEmittedAt >= _minimumInterval;
                if (_observedPercent == 100 || movedEnough || waitedEnough)
                {
                    _lastEmittedPercent = _observedPercent;
                    _lastEmittedAt = now;
                    return CreateEmission(_observedPercent, isHeartbeat: false, now);
                }

                return null;
            }

            if (now - _lastEmittedAt < _heartbeatInterval)
            {
                return null;
            }

            _lastEmittedAt = now;
            return CreateEmission(_observedPercent, isHeartbeat: true, now);
        }
    }

    private CliProgressEmission CreateEmission(int percent, bool isHeartbeat, DateTimeOffset now) =>
        new(
            percent,
            isHeartbeat,
            ElapsedSeconds: Math.Max(0, (int)Math.Floor((now - _startedAt).TotalSeconds)),
            PlateauSeconds: Math.Max(0, (int)Math.Floor((now - _lastObservedChangeAt).TotalSeconds)));
}

internal sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
