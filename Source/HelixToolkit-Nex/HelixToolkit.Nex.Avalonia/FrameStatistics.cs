namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Aggregated per-frame timing statistics for a <see cref="HelixViewport"/>, reported about once per
/// second through <see cref="HelixViewport.FrameStatisticsUpdated"/>.
/// </summary>
/// <param name="Fps">Frames actually rendered and presented per second over the reporting window.</param>
/// <param name="AverageRenderMs">
/// Average time, in milliseconds, spent in the engine's offscreen render (CPU command recording plus
/// submit) per frame.
/// </param>
/// <param name="AveragePresentMs">
/// Average time, in milliseconds, spent awaiting the compositor present (importing/updating the
/// composition surface, including the keyed-mutex/semaphore synchronization) per frame. A present
/// time close to the display refresh interval indicates the engine is being serialized against the
/// compositor read of the shared image.
/// </param>
public readonly record struct FrameStatistics(
    double Fps,
    double AverageRenderMs,
    double AveragePresentMs);
