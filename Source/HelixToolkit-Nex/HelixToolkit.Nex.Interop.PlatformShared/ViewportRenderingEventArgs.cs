using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Rendering;
#if HxWPF
namespace HelixToolkit.Nex.Wpf;

#elif HxWinUI
namespace HelixToolkit.Nex.WinUI;

#endif
/// <summary>
/// Event args passed to <see cref="HelixViewport.Rendering"/> subscribers each frame.
/// The handler must set <see cref="WorldDataProvider"/> so the viewport knows what to render.
/// </summary>
public sealed class ViewportRenderingEventArgs : EventArgs
{
    /// <summary>The per-viewport render context (window size, camera, final output texture).</summary>
    public RenderContext RenderContext { get; }

    /// <summary>
    /// Set this to the <see cref="Engine.WorldDataProvider"/> that should be rendered
    /// for this viewport in the current frame. If left <c>null</c> the frame is skipped.
    /// </summary>
    public WorldDataProvider? WorldDataProvider { get; set; }

    /// <summary>Seconds elapsed since the previous frame.</summary>
    public float DeltaTime { set; get; }

    internal ViewportRenderingEventArgs(RenderContext renderContext)
    {
        RenderContext = renderContext;
    }
}
