namespace HelixToolkit.Nex.Rendering;

public sealed class RenderStatistics
{
    public uint DrawCalls { get; internal set; }

    public void ResetPerFrame()
    {
        DrawCalls = 0;
    }
}
