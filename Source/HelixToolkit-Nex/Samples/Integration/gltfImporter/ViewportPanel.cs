using System.Numerics;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;
using Viewport = HelixToolkit.Nex.ImGui.Viewport;

namespace HelixToolkit.Nex.Sample.GltfImporter;

/// <summary>
/// Renders the offscreen 3D texture as an ImGui image widget and handles
/// viewport-relative mouse input for picking and camera control.
/// </summary>
/// <remarks>
/// This panel delegates the ImGui window, image drawing, and mouse-input handling to the
/// reusable <see cref="Viewport"/> class. It keeps the demo-specific concerns (selection and
/// picking) and exposes the measured content size for render-target allocation.
/// </remarks>
internal class ViewportPanel
{
    private readonly SelectionManager _selectionManager;
    private readonly ICameraController _cameraController;
    private Size _contentSize = new(1, 1);

    // Lazily created reusable viewport. Created on the first Draw call once a RenderContext is
    // available, since the RenderContext is stable across frames.
    private Viewport? _viewport;

    // Per-frame references stored so the pick callback can reach the current engine/context.
    private Engine.Engine? _engine;
    private RenderContext? _renderContext;

    /// <summary>Gets the current content region size for render target allocation.</summary>
    public Size ContentSize => _contentSize;

    public ViewportPanel(SelectionManager selectionManager, ICameraController controller)
    {
        _selectionManager =
            selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
        _cameraController = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>
    /// Draws the viewport image and processes mouse input.
    /// - Left-click: pick entity at cursor position (always active)
    /// - Right-drag: orbit camera (only when a model is loaded)
    /// - Middle-drag: pan camera (only when a model is loaded)
    /// - Scroll wheel: zoom (only when a model is loaded)
    /// </summary>
    /// <param name="engine">The engine instance for creating picking requests.</param>
    /// <param name="offscreenTexture">The offscreen render target texture handle.</param>
    /// <param name="position">The ImGui window position.</param>
    /// <param name="size">The ImGui window size.</param>
    /// <param name="renderContext">The render context for picking operations.</param>
    /// <param name="worldData">The world data provider for entity lookup.</param>
    /// <param name="rootNode">The root node of the scene graph (null if no model loaded).</param>
    public void Draw(
        Engine.Engine engine,
        TextureHandle offscreenTexture,
        Vector2 position,
        Vector2 size,
        RenderContext renderContext,
        WorldDataProvider worldData,
        Node? rootNode = null
    )
    {
        _engine = engine;
        _renderContext = renderContext;

        // Lazily create the reusable viewport. Picking is always active via the callback;
        // camera input is gated per-frame below by toggling the controller.
        _viewport ??= new Viewport(renderContext, _cameraController)
        {
            PickCallback = (x, y) => PerformPick(_engine!, _renderContext!, x, y),
        };

        // Camera input (rotate/pan/zoom) only moves the camera when a model is loaded. Detaching
        // the controller while no model is loaded keeps picking active but suppresses gestures.
        _viewport.CameraController = rootNode is not null ? _cameraController : null;

        _viewport.Draw(offscreenTexture, position, size);

        // Update the content size from the measured viewport size for render-target allocation.
        // ViewportSize is already floored to a minimum of 1 per dimension; retain the previous
        // value if the viewport reported no measurement this frame.
        var measured = _viewport.ViewportSize;
        _contentSize = (measured.Width > 0 && measured.Height > 0) ? measured : _contentSize;
    }

    private void PerformPick(Engine.Engine engine, RenderContext context, int x, int y)
    {
        engine.CreatePickingRequest(context, new Vector2(x, y), HandlePickingResponse);
    }

    private void HandlePickingResponse(PickingResponse response)
    {
        _selectionManager.Deselect();
        if (response.TryGetPickingResult(out var result))
        {
            var entity = result.Entity;
            if (entity.Valid)
            {
                var node = Node.FindNode(entity);
                _selectionManager.Select(entity, node);
            }
        }
    }
}
