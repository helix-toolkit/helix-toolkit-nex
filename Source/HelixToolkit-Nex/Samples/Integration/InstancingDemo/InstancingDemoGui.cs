using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace InstancingDemo;

/// <summary>
/// ImGui drawing for the instancing demo: a top menu bar, a left control panel (instancing info,
/// animation control, and picking readout) and the 3D <c>Viewport</c> filling the remaining area.
/// </summary>
internal partial class InstancingDemo
{
    private const float ControlPanelWidth = 320f;

    private void DrawGui(TextureHandle offscreenTex, float displayWidth, float displayHeight)
    {
        float menuBarHeight = DrawMainMenuBar();

        var panelPos = new Vector2(0f, menuBarHeight);
        var panelSize = new Vector2(ControlPanelWidth, displayHeight - menuBarHeight);
        DrawControlPanel(panelPos, panelSize);

        var viewportPos = new Vector2(ControlPanelWidth, menuBarHeight);
        var viewportSize = new Vector2(
            displayWidth - ControlPanelWidth,
            displayHeight - menuBarHeight
        );
        _viewport?.Draw(offscreenTex, viewportPos, viewportSize);

        // Keep the offscreen render size in sync with the measured viewport region so the camera
        // aspect ratio matches the drawn image.
        if (_viewport is not null)
        {
            var measured = _viewport.ViewportSize;
            if (measured.Width > 0 && measured.Height > 0)
            {
                _viewportSize = measured;
            }
        }
    }

    private static float DrawMainMenuBar()
    {
        float height = 0f;
        if (Gui.BeginMainMenuBar())
        {
            height = Gui.GetWindowHeight();
            if (Gui.BeginMenu("File"))
            {
                if (Gui.MenuItem("Quit"))
                {
                    Environment.Exit(0);
                }
                Gui.EndMenu();
            }
            if (Gui.BeginMenu("View"))
            {
                Gui.MenuItem("Instancing Integration Demo", string.Empty, true, false);
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }
        return height;
    }

    private void DrawControlPanel(Vector2 pos, Vector2 size)
    {
        Gui.SetNextWindowPos(pos, ImGuiCond.Always);
        Gui.SetNextWindowSize(size, ImGuiCond.Always);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (!Gui.Begin("Instancing Controls##Panel", flags))
        {
            Gui.End();
            return;
        }

        Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "GPU Instancing");
        Gui.Separator();
        Gui.Spacing();

        Gui.TextWrapped(
            "Two instanced mesh types share one scene. The orange boxes use a static instance "
                + "buffer; the blue spheres recompute their per-instance transforms every frame."
        );
        Gui.Spacing();

        if (Gui.CollapsingHeader("Mouse", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.BulletText("Left click: pick / highlight an instance");
            Gui.BulletText("Right drag: orbit camera");
            Gui.BulletText("Middle drag: pan camera");
            Gui.BulletText("Wheel: zoom");
        }

        Gui.Spacing();

        if (Gui.CollapsingHeader("Instancing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int staticCount = _staticCount;
            Gui.Text("Static (box) instances");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderInt("##StaticCount", ref staticCount, MinInstanceCount, MaxInstanceCount))
            {
                SetStaticCount(staticCount);
            }

            int dynamicCount = _dynamicCount;
            Gui.Text("Dynamic (sphere) instances");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderInt("##DynamicCount", ref dynamicCount, MinInstanceCount, MaxInstanceCount))
            {
                SetDynamicCount(dynamicCount);
            }

            Gui.Spacing();
            Gui.Text($"Animation time: {_animationTime:F2}s");
            if (
                Gui.Button(
                    _animationPaused ? "Resume animation" : "Pause animation",
                    new Vector2(-1, 0)
                )
            )
            {
                _animationPaused = !_animationPaused;
            }
        }

        Gui.Spacing();

        if (Gui.CollapsingHeader("Picking", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_selectedEntity.Valid)
            {
                string meshName =
                    _selectedEntity == (_staticNode?.Entity ?? default) ? "Static box"
                    : _selectedEntity == (_dynamicNode?.Entity ?? default) ? "Dynamic sphere"
                    : "Unknown";

                Gui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), $"Selected: {meshName}");
                Gui.Text($"Kind: {_selectedKind}");
                Gui.Text($"Instance index: {_selectedInstanceId}");
                Gui.Text(
                    $"World pos: ({_selectedWorldPosition.X:F2}, "
                        + $"{_selectedWorldPosition.Y:F2}, {_selectedWorldPosition.Z:F2})"
                );
                Gui.Spacing();
                if (Gui.Button("Clear selection", new Vector2(-1, 0)))
                {
                    ClearSelection();
                }
            }
            else
            {
                Gui.TextDisabled("Left-click an instance in the viewport to highlight it.");
            }
        }

        Gui.End();
    }
}
