using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Interop;

/// <summary>
/// Provides per-frame data and updates for a <c>HelixViewport</c>.
/// <para>
/// Set an implementation on the viewport's <c>ViewportClient</c> dependency property.
/// The viewport calls <see cref="Update"/> each frame before rendering and reads
/// <see cref="DataProvider"/> to obtain the scene data. If <see cref="DataProvider"/>
/// returns <c>null</c>, the frame is skipped.
/// </para>
/// <para>
/// This replaces the previous pattern of mutating <see cref="ViewportRenderingEventArgs"/>
/// inside a <c>BeforeRender</c> event handler.
/// </para>
/// </summary>
public interface IViewportClient
{
    /// <summary>
    /// Gets the render data provider for the current frame.
    /// Return <c>null</c> to skip the frame.
    /// </summary>
    IRenderDataProvider? DataProvider { get; }

    /// <summary>
    /// Called once per frame before rendering. Use this to update the camera,
    /// tick animations, or perform any other per-frame work.
    /// </summary>
    /// <param name="context">The per-viewport render context (window size, camera, output texture).</param>
    /// <param name="deltaTime">Seconds elapsed since the previous frame.</param>
    ICameraParamsProvider Update(RenderContext context, float deltaTime);
}
