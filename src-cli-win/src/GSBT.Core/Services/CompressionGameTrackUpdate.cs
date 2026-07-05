namespace GSBT.Core.Services;

/// <summary>Per-game compression position for screen-saver file tracking (per-file / non-solid archives).</summary>
public readonly record struct CompressionGameTrackUpdate(
    string Previous,
    string Current,
    string Upcoming);
