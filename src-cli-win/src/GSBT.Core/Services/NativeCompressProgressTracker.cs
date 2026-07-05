namespace GSBT.Core.Services;

/// <summary>Byte- and file-weighted UI percent; capped at 99 until the archive is finalized.</summary>
internal sealed class NativeCompressProgressTracker
{
    private readonly long _totalBytes;
    private readonly int _fileCount;
    private long _completedBytes;
    private long _currentFileSize;
    private int _filesFinished;

    public NativeCompressProgressTracker(long totalBytes, int fileCount)
    {
        _totalBytes = totalBytes;
        _fileCount = fileCount;
    }

    public void OnFileStarted(long fileSize) => _currentFileSize = fileSize;

    public void OnFileFinished()
    {
        _completedBytes += _currentFileSize;
        _currentFileSize = 0;
        _filesFinished++;
    }

    public int ComputePercent(int withinFilePercent)
    {
        withinFilePercent = Math.Clamp(withinFilePercent, 0, 100);
        if (_totalBytes > 0)
        {
            var current = (long)(_currentFileSize * (withinFilePercent / 100.0));
            return (int)Math.Min(99, ((_completedBytes + current) * 100) / _totalBytes);
        }

        if (_fileCount > 0)
        {
            var blended = _filesFinished + (withinFilePercent / 100.0);
            return (int)Math.Min(99, blended * 100 / _fileCount);
        }

        return Math.Min(99, withinFilePercent);
    }
}
