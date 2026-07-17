using GSBT.Cli.Output;

namespace GSBT.Core.Tests;

public sealed class CliProgressEventThrottleTests
{
    [Fact]
    public void Duplicate_percentages_are_suppressed()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = CreateThrottle(() => now);

        Assert.NotNull(throttle.Observe(0));
        Assert.Null(throttle.Observe(0));
        Assert.Null(throttle.Observe(0));
    }

    [Fact]
    public void Meaningful_jump_is_emitted_without_waiting()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = CreateThrottle(() => now);

        Assert.NotNull(throttle.Observe(0));
        var emission = throttle.Observe(5);

        Assert.NotNull(emission);
        Assert.Equal(5, emission.Value.Percent);
        Assert.False(emission.Value.IsHeartbeat);
    }

    [Fact]
    public void Small_movement_is_emitted_after_minimum_interval()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = CreateThrottle(() => now);

        Assert.NotNull(throttle.Observe(10));
        Assert.Null(throttle.Observe(11));
        now += TimeSpan.FromMilliseconds(250);
        var emission = throttle.Observe(11);

        Assert.NotNull(emission);
        Assert.Equal(11, emission.Value.Percent);
    }

    [Fact]
    public void Plateau_emits_periodic_heartbeat_with_duration()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = CreateThrottle(() => now);

        Assert.NotNull(throttle.Observe(99));
        now += TimeSpan.FromSeconds(14);
        Assert.Null(throttle.Observe(99));
        now += TimeSpan.FromSeconds(1);
        var heartbeat = throttle.Observe(99);

        Assert.NotNull(heartbeat);
        Assert.True(heartbeat.Value.IsHeartbeat);
        Assert.Equal(99, heartbeat.Value.Percent);
        Assert.Equal(15, heartbeat.Value.PlateauSeconds);
    }

    [Fact]
    public void Late_regression_never_moves_agent_progress_backwards()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = CreateThrottle(() => now);

        Assert.NotNull(throttle.Observe(50));
        now += TimeSpan.FromMilliseconds(250);
        Assert.Null(throttle.Observe(40));
        now += TimeSpan.FromSeconds(15);
        var heartbeat = throttle.Observe(40);

        Assert.NotNull(heartbeat);
        Assert.Equal(50, heartbeat.Value.Percent);
    }

    private static CliProgressEventThrottle CreateThrottle(Func<DateTimeOffset> clock) =>
        new(
            clock,
            minimumInterval: TimeSpan.FromMilliseconds(250),
            heartbeatInterval: TimeSpan.FromSeconds(15));
}
