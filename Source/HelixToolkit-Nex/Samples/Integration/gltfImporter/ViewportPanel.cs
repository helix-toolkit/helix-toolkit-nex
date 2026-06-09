using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Sample.GltfImporter;

/// <summary>
/// Renders the offscreen 3D texture as an ImGui image widget and handles
/// viewport-relative mouse input for picking and camera control.
/// </summary>
internal class ViewportPanel
{
    private readonly SelectionManager _selectionManager;
    private readonly OrbitCameraController _cameraController;
    private Size _contentSize = new(1, 1);
    private bool _isRotating;
    private bool _isPanning;

    /// <summary>Gets the current content region size for render target allocation.</summary>
    public Size ContentSize => _contentSize;

    public ViewportPanel(SelectionManager selectionManager, OrbitCameraController controller)
    {
        _selectionManager =
            selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
        _cameraController = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>
    /// Draws the viewport image and processes mouse input.
    /// - Left-click: pick entity at cursor position
    /// - Right-drag: orbit camera
    /// - Middle-drag: pan camera
    /// - Scroll wheel: zoom
    /// </summary>
    /// <param name="offscreenTexture">The offscreen render target texture handle.</param>
    /// <param name="position">The ImGui window position.</param>
    /// <param name="size">The ImGui window size.</param>
    /// <param name="renderContext">The render context for picking operations.</param>
    /// <param name="context">The graphics context for raw pick operations.</param>
    /// <param name="worldData">The world data provider for entity lookup.</param>
    /// <param name="rootNode">The root node of the scene graph (null if no model loaded).</param>
    public void Draw(
        TextureHandle offscreenTexture,
        Vector2 position,
        Vector2 size,
        RenderContext renderContext,
        IContext context,
        WorldDataProvider worldData,
        Node? rootNode = null
    )
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        Gui.SetNextWindowPos(position);
        Gui.SetNextWindowSize(size);
        Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin("##Viewport", windowFlags);
        Gui.PopStyleVar();

        var contentSize = Gui.GetContentRegionAvail();

        // Clamp each dimension independently to minimum 1 pixel (Req 8.3, 8.4)
        int clampedWidth = Math.Max(1, (int)contentSize.X);
        int clampedHeight = Math.Max(1, (int)contentSize.Y);
        _contentSize = new Size(clampedWidth, clampedHeight);

        // Render the offscreen texture as an ImGui image sized to the content region
        var displaySize = new Vector2(
            contentSize.X > 0 ? contentSize.X : 1,
            contentSize.Y > 0 ? contentSize.Y : 1
        );
        var canvasPos = Gui.GetCursorScreenPos();
        Gui.Image((nint)offscreenTexture.Index, displaySize, Vector2.Zero, Vector2.One);

        bool hovered = Gui.IsItemHovered();

        if (hovered)
        {
            var mousePos = Gui.GetMousePos();
            // Compute viewport-relative mouse position by subtracting the ImGui window position
            var relativePos = new Vector2(mousePos.X - canvasPos.X, mousePos.Y - canvasPos.Y);

            // Left-click: picking (Req 4.1, 4.4)
            if (Gui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                PerformPick(
                    (int)relativePos.X,
                    (int)relativePos.Y,
                    renderContext,
                    context,
                    worldData,
                    rootNode
                );
            }

            // Camera input only works when a model is loaded (Req 5.5)
            if (rootNode is not null)
            {
                // Right-click: begin orbit (Req 5.1)
                if (Gui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _isRotating = true;
                    _cameraController.OnRotateBegin(relativePos.X, relativePos.Y);
                }

                // Middle-click: begin pan (Req 5.2)
                if (Gui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    _isPanning = true;
                    _cameraController.OnPanBegin(relativePos.X, relativePos.Y);
                }

                // Mouse drag: forward to camera controller
                if (_isRotating)
                {
                    _cameraController.OnRotateDelta(relativePos.X, relativePos.Y);
                }
                if (_isPanning)
                {
                    _cameraController.OnPanDelta(relativePos.X, relativePos.Y);
                }

                // Scroll wheel: zoom (Req 5.3)
                var io = Gui.GetIO();
                if (MathF.Abs(io.MouseWheel) > 0.001f)
                {
                    _cameraController.OnZoomDelta(io.MouseWheel);
                }
            }
        }

        // Release drag state on mouse-up (even if not hovered, to avoid stuck drags)
        if (Gui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            _isRotating = false;
        }
        if (Gui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            _isPanning = false;
        }

        Gui.End();
    }

    /// <summary>
    /// Performs a pick operation at the given viewport-relative coordinates.
    /// On hit: selects the entity. On miss: deselects.
    /// </summary>
    private void PerformPick(
        int x,
        int y,
        RenderContext renderContext,
        IContext context,
        WorldDataProvider worldData,
        Node? rootNode
    )
    {
        if (renderContext.ResourceSet is null)
            return;

        // Use the RenderContext extension method for picking
        if (
            !renderContext.TryPickRaw(
                x,
                y,
                out var worldId,
                out var entityId,
                out var instanceIdx,
                out var primitiveId
            )
        )
        {
            // Pick miss: deselect (Req 4.4)
            _selectionManager.Deselect();
            return;
        }
        if (worldId != worldData.World.Id)
        {
            _selectionManager.Deselect();
            return;
        }
        // Pick hit: select the entity (Req 4.1)
        var entity = worldData.World.GetEntity((int)entityId);
        if (!entity.Valid)
        {
            _selectionManager.Deselect();
            return;
        }

        // Find the corresponding Node for the entity by traversing the scene tree
        var node = FindNodeByEntity(rootNode, entity);
        _selectionManager.Select(entity, node);
    }

    /// <summary>
    /// Recursively searches the scene tree for a node matching the given entity.
    /// Returns null if no matching node is found.
    /// </summary>
    private static Node? FindNodeByEntity(Node? root, Entity entity)
    {
        if (root is null)
            return null;

        if (root.Entity == entity)
            return root;

        var children = root.Children;
        if (children is null)
            return null;

        for (int i = 0; i < children.Count; i++)
        {
            var found = FindNodeByEntity(children[i], entity);
            if (found is not null)
                return found;
        }

        return null;
    }
}
