using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Interop;

/// <summary>
/// Event args passed to <see cref="HelixViewport.Rendering"/> subscribers each frame.
/// The handler must set <see cref="DataProvider"/> so the viewport knows what to render.
/// </summary>
public sealed class ViewportRenderingEventArgs : EventArgs
{
    /// <summary>The per-viewport render context (window size, camera, final output texture).</summary>
    public RenderContext RenderContext { get; }

    /// <summary>
    /// Set this to the <see cref="IRenderDataProvider"/> that should be rendered
    /// for this viewport in the current frame. If left <c>null</c> the frame is skipped.
    /// </summary>
    public IRenderDataProvider? DataProvider { get; set; }

    /// <summary>Seconds elapsed since the previous frame.</summary>
    public float DeltaTime { set; get; }

    public ViewportRenderingEventArgs(RenderContext renderContext)
    {
        RenderContext = renderContext;
    }
}
