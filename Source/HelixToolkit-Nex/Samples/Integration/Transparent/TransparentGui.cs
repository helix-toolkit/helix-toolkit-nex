using System.Numerics;
using HelixToolkit.Nex.Maths;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace Transparent;

internal partial class TransparentDemo
{
    private const float ControlPanelWidth = 340f;

    private void DrawGui(TextureHandle offscreenTex, float displayWidth, float displayHeight)
    {
        var menuBarHeight = DrawMainMenuBar();

        // Layout: Control panel on the left, 3D viewport on the right
        var panelPos = new Vector2(0, menuBarHeight);
        var panelSize = new Vector2(ControlPanelWidth, displayHeight - menuBarHeight);
        DrawControlPanel(panelPos, panelSize);

        var viewportPos = new Vector2(ControlPanelWidth, menuBarHeight);
        var viewportSize = new Vector2(
            displayWidth - ControlPanelWidth,
            displayHeight - menuBarHeight
        );
        Draw3DViewport(offscreenTex, viewportPos, viewportSize);
    }

    private float DrawMainMenuBar()
    {
        float height = 0;
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
                Gui.MenuItem("OIT Demo", string.Empty, true, false);
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

        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (Gui.Begin("OIT Transparency Controls", flags))
        {
            Gui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Order-Independent Transparency");
            Gui.Separator();
            Gui.Spacing();

            Gui.Text("Adjust the opacity and color of each");
            Gui.Text("transparent object using the sliders below.");
            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();

            // --- Transparent Objects ---
            if (Gui.CollapsingHeader("Transparent Objects", ImGuiTreeNodeFlags.DefaultOpen))
            {
                for (int i = 0; i < _transparentObjects.Count; i++)
                {
                    var obj = _transparentObjects[i];
                    Gui.PushID(i);

                    if (Gui.TreeNodeEx(obj.Name, ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        // Opacity slider
                        var opacity = obj.Opacity;
                        if (Gui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f))
                        {
                            obj.Opacity = opacity;
                            obj.MaterialProperties.Opacity = opacity;
                        }

                        // Color picker
                        var color = obj.Albedo;
                        if (Gui.ColorEdit3("Color", ref color))
                        {
                            obj.Albedo = color;
                            obj.MaterialProperties.Properties.Albedo = color;
                            obj.MaterialProperties.NotifyUpdated();
                        }

                        // Roughness
                        var roughness = obj.MaterialProperties.Roughness;
                        if (Gui.SliderFloat("Roughness", ref roughness, 0.0f, 1.0f))
                        {
                            obj.MaterialProperties.Roughness = roughness;
                        }

                        // Metallic
                        var metallic = obj.MaterialProperties.Metallic;
                        if (Gui.SliderFloat("Metallic", ref metallic, 0.0f, 1.0f))
                        {
                            obj.MaterialProperties.Metallic = metallic;
                        }

                        Gui.TreePop();
                    }

                    Gui.PopID();
                    Gui.Spacing();
                }
            }

            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();

            // --- Post Effects ---
            if (Gui.CollapsingHeader("Post Effects", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var smaaEnabled = _smaa.Enabled;
                if (Gui.Checkbox("SMAA Anti-Aliasing", ref smaaEnabled))
                {
                    _smaa.Enabled = smaaEnabled;
                }

                var fxaaEnabled = _fxaa.Enabled;
                if (Gui.Checkbox("FXAA Anti-Aliasing", ref fxaaEnabled))
                {
                    _fxaa.Enabled = fxaaEnabled;
                }

                var tonemapEnabled = _toneMappingNode.Enabled;
                if (Gui.Checkbox("Tone Mapping", ref tonemapEnabled))
                {
                    _toneMappingNode.Enabled = tonemapEnabled;
                }

                var showFps = _showFPS.Enabled;
                if (Gui.Checkbox("Show FPS", ref showFps))
                {
                    _showFPS.Enabled = showFps;
                }
            }

            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();

            // --- Presets ---
            if (Gui.CollapsingHeader("Presets"))
            {
                if (Gui.Button("All Glass-like", new Vector2(-1, 0)))
                {
                    foreach (var obj in _transparentObjects)
                    {
                        obj.Opacity = 0.2f;
                        obj.MaterialProperties.Opacity = 0.2f;
                        obj.MaterialProperties.Roughness = 0.05f;
                        obj.MaterialProperties.Metallic = 0.0f;
                    }
                }
                if (Gui.Button("All Semi-Opaque", new Vector2(-1, 0)))
                {
                    foreach (var obj in _transparentObjects)
                    {
                        obj.Opacity = 0.7f;
                        obj.MaterialProperties.Opacity = 0.7f;
                        obj.MaterialProperties.Roughness = 0.5f;
                    }
                }
                if (Gui.Button("All Fully Transparent", new Vector2(-1, 0)))
                {
                    foreach (var obj in _transparentObjects)
                    {
                        obj.Opacity = 0.05f;
                        obj.MaterialProperties.Opacity = 0.05f;
                    }
                }
                if (Gui.Button("Reset Defaults", new Vector2(-1, 0)))
                {
                    ResetDefaults();
                }
            }
        }
        Gui.End();
    }

    private void Draw3DViewport(TextureHandle offscreenTex, Vector2 pos, Vector2 size)
    {
        Gui.SetNextWindowPos(pos, ImGuiCond.Always);
        Gui.SetNextWindowSize(size, ImGuiCond.Always);

        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        if (Gui.Begin("3D Viewport", flags))
        {
            var contentSize = Gui.GetContentRegionAvail();

            if (contentSize.X > 1 && contentSize.Y > 1)
            {
                _viewportSize = new Size((int)contentSize.X, (int)contentSize.Y);
            }

            // Draw the offscreen texture
            var cursorPos = Gui.GetCursorScreenPos();
            Gui.Image((nint)offscreenTex.Index, contentSize, Vector2.Zero, Vector2.One);

            // Handle mouse input inside viewport
            if (Gui.IsItemHovered())
            {
                var io = Gui.GetIO();
                var mousePos = io.MousePos;
                var viewportX = mousePos.X - cursorPos.X;
                var viewportY = mousePos.Y - cursorPos.Y;

                // Mouse buttons
                if (Gui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    OnViewportMouseDown(1, viewportX, viewportY);
                }
                if (Gui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    OnViewportMouseDown(2, viewportX, viewportY);
                }

                // Mouse wheel
                if (io.MouseWheel != 0)
                {
                    OnViewportMouseWheel(io.MouseWheel);
                }

                // Mouse move (always forward while dragging)
                OnViewportMouseMove(viewportX, viewportY);
            }

            // Release buttons globally
            if (Gui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                OnViewportMouseUp(1);
            }
            if (Gui.IsMouseReleased(ImGuiMouseButton.Middle))
            {
                OnViewportMouseUp(2);
            }
        }
        Gui.End();
    }

    private void ResetDefaults()
    {
        // Reset to initial opacity/color values
        float[] defaultOpacities = [0.4f, 0.5f, 0.6f, 0.35f, 0.45f, 0.25f];
        Vector3[] defaultColors =
        [
            new(1.0f, 0.1f, 0.1f),
            new(0.1f, 1.0f, 0.1f),
            new(0.1f, 0.2f, 1.0f),
            new(1.0f, 0.9f, 0.1f),
            new(0.1f, 0.9f, 0.9f),
            new(0.6f, 0.1f, 0.8f),
        ];

        for (int i = 0; i < _transparentObjects.Count && i < defaultOpacities.Length; i++)
        {
            var obj = _transparentObjects[i];
            obj.Opacity = defaultOpacities[i];
            obj.Albedo = defaultColors[i];
            obj.MaterialProperties.Properties.Albedo = defaultColors[i];
            obj.MaterialProperties.Opacity = defaultOpacities[i];
            obj.MaterialProperties.Roughness = 0.3f;
            obj.MaterialProperties.Metallic = 0.0f;
        }
    }
}
