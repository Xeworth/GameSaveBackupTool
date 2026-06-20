using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class NativeCompressProgressTrackerTests
{
    [Fact]
    public void Byte_weighted_progress_moves_with_files_and_stays_below_100()
    {
        var tracker = new NativeCompressProgressTracker(1000, 2);
        Assert.Equal(0, tracker.ComputePercent(0));

        tracker.OnFileStarted(400);
        Assert.Equal(20, tracker.ComputePercent(50));
        tracker.OnFileFinished();
        Assert.Equal(40, tracker.ComputePercent(0));

        tracker.OnFileStarted(600);
        Assert.Equal(70, tracker.ComputePercent(50));
        tracker.OnFileFinished();
        Assert.Equal(99, tracker.ComputePercent(100));
    }

    [Fact]
    public void File_count_fallback_when_total_bytes_zero()
    {
        var tracker = new NativeCompressProgressTracker(0, 4);
        tracker.OnFileStarted(0);
        Assert.Equal(12, tracker.ComputePercent(50));
        tracker.OnFileFinished();
        Assert.Equal(25, tracker.ComputePercent(0));
    }
}
