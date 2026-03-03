namespace HelixToolkit.Nex.Rendering;

public sealed class RenderStatistics
{
    private static readonly ILogger _logger = LogManager.Create<RenderStatistics>();
    private readonly RingBuffer<long> _frameTimestamps = new(1000);

    public uint DrawCalls { get; internal set; }

    public long LastRenderTime { get; internal set; } = 0;

    public long LastUpdateTime { get; internal set; } = 0;

    public uint UpdateDurationSecond { set; get; } = 1;

    public float AverageRenderDurationMs { private set; get; } = 0;

    public float FramesPerSecond =>
        AverageRenderDurationMs > 0 ? 1000f / AverageRenderDurationMs : 0;

    public void ResetPerFrame()
    {
        DrawCalls = 0;
        Update();
    }

    public void AddFrameTimeStamp()
    {
        _frameTimestamps.Push(Stopwatch.GetTimestamp());
    }

    private void Update()
    {
        var time = Stopwatch.GetTimestamp();
        if (time - LastUpdateTime < UpdateDurationSecond * Stopwatch.Frequency)
        {
            return;
        }
        LastUpdateTime = time;
        var count = _frameTimestamps.Count;
        if (count > 1)
        {
            float totalDeltaMs = 0f;
            int deltas = 0;
            long prev = 0;
            while (_frameTimestamps.TryPop(out var ts))
            {
                if (prev != 0)
                {
                    totalDeltaMs += (ts - prev) / (float)Stopwatch.Frequency * 1000f;
                    deltas++;
                }
                prev = ts;
            }
            AverageRenderDurationMs = deltas > 0 ? totalDeltaMs / deltas : 0;
            if (RenderSettings.LogFPSInDebug)
            {
                _logger.LogDebug("FPS: {FramesPerSecond}", FramesPerSecond);
            }
        }
        else
        {
            // Drain the single entry without updating the average
            _frameTimestamps.TryPop(out _);
        }
    }
}
