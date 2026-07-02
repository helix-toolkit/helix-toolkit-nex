using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Gizmos;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

/// <summary>
/// ImGui control-panel drawing for <see cref="GizmoDemo"/>. Kept in a partial file so the render/setup
/// logic and the UI stay separated. Mirrors <c>PickingDemo.DrawGui</c>'s left-panel + viewport layout.
/// </summary>
internal sealed partial class GizmoDemo
{
    private void DrawGui(int width, int height, Handle<Texture> offscreenTex)
    {
        if (Gui.BeginMainMenuBar())
        {
            if (Gui.BeginMenu("File"))
            {
                if (Gui.MenuItem("Quit"))
                    Environment.Exit(0);
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }

        const float PanelWidth = 320f;
        Gui.SetNextWindowPos(new Vector2(0, Gui.GetFrameHeight()), ImGuiCond.Always);
        Gui.SetNextWindowSize(
            new Vector2(PanelWidth, height - Gui.GetFrameHeight()),
            ImGuiCond.Always
        );

        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (Gui.Begin("Gizmo Controls##Panel", flags) && _gizmoManager is not null)
        {
            Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Gizmo Mode");
            Gui.Separator();
            if (Gui.RadioButton("Translate", _gizmoMode == GizmoMode.Translate))
            {
                _gizmoMode = GizmoMode.Translate;
                ApplyDefinition();
            }
            Gui.SameLine();
            if (Gui.RadioButton("Rotate", _gizmoMode == GizmoMode.Rotate))
            {
                _gizmoMode = GizmoMode.Rotate;
                ApplyDefinition();
            }
            Gui.SameLine();
            if (Gui.RadioButton("Scale", _gizmoMode == GizmoMode.Scale))
            {
                _gizmoMode = GizmoMode.Scale;
                ApplyDefinition();
            }

            Gui.Spacing();
            Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Gizmo Space");
            Gui.Separator();
            if (Gui.RadioButton("World", _gizmoSpace == GizmoSpace.World))
            {
                _gizmoSpace = GizmoSpace.World;
                ApplyDefinition();
            }
            Gui.SameLine();
            if (Gui.RadioButton("Local", _gizmoSpace == GizmoSpace.Local))
            {
                _gizmoSpace = GizmoSpace.Local;
                ApplyDefinition();
            }

            Gui.Spacing();
            Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Occlusion Mode");
            Gui.Separator();
            if (
                Gui.RadioButton(
                    "Always On Top",
                    _occlusionMode == GizmoOcclusionMode.AlwaysOnTop
                )
            )
                SetOcclusionMode(GizmoOcclusionMode.AlwaysOnTop);
            Gui.SameLine();
            if (
                Gui.RadioButton(
                    "Depth Tested",
                    _occlusionMode == GizmoOcclusionMode.DepthTested
                )
            )
                SetOcclusionMode(GizmoOcclusionMode.DepthTested);

            Gui.Spacing();
            Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Handle Sizing");
            Gui.Separator();
            if (Gui.SliderFloat("Handle Size (px)", ref _handlePixelSize, 1f, 1000f))
                ApplyDefinition();

            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();
            Gui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Target");
            var pos = _targetWorldMatrix.Translation;
            Gui.Text($"Position: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            Gui.Text($"Dragging: {(_gizmoManager.IsDragging ? "yes" : "no")}");
            if (Gui.Button("Reset Target"))
            {
                _targetWorldMatrix = InitialTargetMatrix;
                ApplyTargetTransform();
            }

            Gui.Spacing();
            Gui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Last Pick");
            Gui.TextWrapped(_lastPickInfo);

            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();
            Gui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), "Controls");
            Gui.BulletText("Left click handle: drag");
            Gui.BulletText("Right drag: rotate camera");
            Gui.BulletText("Middle drag: pan");
            Gui.BulletText("Scroll: zoom");
        }
        Gui.End();

        var viewportPos = new Vector2(PanelWidth, Gui.GetFrameHeight());
        var viewportSize = new Vector2(width - PanelWidth, height - Gui.GetFrameHeight());
        _viewport?.Draw(offscreenTex, viewportPos, viewportSize);
        if (_viewport is not null)
        {
            var m = _viewport.ViewportSize;
            if (m.Width > 0 && m.Height > 0)
                _viewportSize = m;
        }

        UpdateGizmoHover();
        DriveGizmoDrag();
    }

    /// <summary>
    /// Highlights the gizmo handle under the pointer via the asynchronous picking path. Each frame
    /// (when not dragging and the viewport is hovered) it schedules a throttled hover pick; the
    /// callback (<c>OnHoverPickResponse</c>) routes the decoded pixel to
    /// <see cref="GizmoManager.ResolveHighlight"/> so the render node draws the resolved handle in the
    /// highlight color. Clears the highlight (and the in-flight throttle) when the pointer leaves the
    /// viewport or a drag is active.
    /// </summary>
    private void UpdateGizmoHover()
    {
        if (_gizmoManager is null || _viewport is null)
            return;

        // While dragging, the active handle is already highlighted; leave the hover state alone and
        // clear the throttle so hovering resumes cleanly once the drag ends.
        if (_gizmoManager.IsDragging)
        {
            _hoverPickInFlight = false;
            return;
        }

        if (!_viewport.IsHovered)
        {
            // Clear any highlight across tracked gizmos when the pointer leaves the viewport.
            _gizmoManager.ResolveHighlight(false, 0u, default);
            _hoverPickInFlight = false;
            return;
        }

        var p = _viewport.RelativePointer;
        RequestHoverPick((int)p.X, (int)p.Y);
    }

    /// <summary>
    /// Sets the occlusion mode: updates the definition state (so the republished component carries the
    /// per-handle occlusion) and the render node's node-level occlusion (which selects the pass depth
    /// state). Kept in sync so both the shader flag and the depth test agree.
    /// </summary>
    private void SetOcclusionMode(GizmoOcclusionMode mode)
    {
        _occlusionMode = mode;
        if (_gizmoNode is not null)
            _gizmoNode.OcclusionMode = mode;
        ApplyDefinition();
    }

    /// <summary>
    /// Continuous drag driving via ImGui IO button state (SDL mouse events are routed into ImGui). The
    /// Viewport <c>PickCallback</c> only fires on click, so pointer-move/up during a drag are handled
    /// here: while the left button is held we unproject the current viewport pointer to a ray and apply
    /// the gizmo's incremental transform delta; on release we end the drag.
    /// </summary>
    private void DriveGizmoDrag()
    {
        if (_gizmoManager is null || _renderContext is null || _viewport is null)
            return;

        // The drag is owned by the engine-hosted gizmo service (begun by automatic pick routing). Drive
        // it while it is active and the left button is held; end it on release.
        if (!_gizmoManager.IsDragging)
            return;

        var io = Gui.GetIO();
        bool leftDown = io.MouseDown[0];

        if (leftDown)
        {
            var p = _viewport.RelativePointer;
            if (_renderContext.TryUnProject(p.X, p.Y, out var ray))
            {
                if (_gizmoManager.UpdateDrag(ray, out var delta))
                {
                    // System.Numerics row-vector convention (matches the gizmo design example).
                    _targetWorldMatrix = delta * _targetWorldMatrix;
                }
            }
        }
        else
        {
            _gizmoManager.EndDrag();
        }
    }
}
