using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Interop;

/// <summary>
/// Read-only event args raised by <c>HelixViewport.BeforeRender</c> each frame.
/// <para>
/// This event is a <b>notification only</b> — subscribers can use it for diagnostics,
/// debug overlays, or other optional per-frame work. The required per-frame data
/// (camera, scene data) should be provided via <see cref="IViewportClient"/> instead.
/// </para>
/// </summary>
public sealed class ViewportRenderingEventArgs : EventArgs
{
    /// <summary>The per-viewport render context (window size, camera, final output texture).</summary>
    public RenderContext RenderContext { get; }

    /// <summary>Seconds elapsed since the previous frame.</summary>
    public float DeltaTime { get; internal set; }

    public ViewportRenderingEventArgs(RenderContext renderContext)
    {
        RenderContext = renderContext;
    }
}
