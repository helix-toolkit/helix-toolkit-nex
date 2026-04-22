using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace PBRTest;

/// <summary>
/// ImGui drawing methods for the PBR material test demo.
/// </summary>
internal partial class PBRDemo
{
    private const float ControlPanelWidth = 360f;

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
        Draw3DViewport(offscreenTex, viewportPos, viewportSize);
    }

    private float DrawMainMenuBar()
    {
        float height = 0f;
        if (Gui.BeginMainMenuBar())
        {
            height = Gui.GetWindowHeight();
            if (Gui.BeginMenu("File"))
            {
                if (Gui.MenuItem("Quit"))
                    Environment.Exit(0);
                Gui.EndMenu();
            }
            if (Gui.BeginMenu("View"))
            {
                Gui.MenuItem("PBR Material Test", string.Empty, true, false);
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

        if (!Gui.Begin("PBR Controls##Panel", flags))
        {
            Gui.End();
            return;
        }

        Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "PBR Material Properties");
        Gui.Separator();
        Gui.Spacing();

        Gui.TextWrapped(
            "The grid shows spheres varying Metallic (rows, bottom=0 top=1) "
                + "and Roughness (columns, left=0 right=1)."
        );
        Gui.Spacing();
        Gui.Separator();
        Gui.Spacing();

        // ----------------------------------------------------------------
        // Sphere grid selector
        // ----------------------------------------------------------------
        if (Gui.CollapsingHeader("Sphere Grid", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.TextDisabled($"Grid: {GridRows} rows (metallic) x {GridCols} cols (roughness)");
            Gui.Spacing();

            for (int row = GridRows - 1; row >= 0; row--)
            {
                for (int col = GridCols - 1; col >= 0; col--)
                {
                    int idx = row * GridCols + col;
                    var isSelected = idx == _selectedIndex;

                    if (isSelected)
                        Gui.PushStyleColor(ImGuiCol.Button, new Vector4(0.9f, 0.5f, 0.1f, 1f));

                    if (Gui.Button($"##sphere_{idx}", new Vector2(36, 36)))
                    {
                        _selectedIndex = idx;
                        _spheres[idx].PullFromMaterial();
                    }

                    if (isSelected)
                        Gui.PopStyleColor();

                    if (Gui.IsItemHovered())
                    {
                        Gui.BeginTooltip();
                        Gui.Text(_spheres[idx].Name);
                        Gui.Text(
                            $"Metallic: {_spheres[idx].MaterialProperties.Properties.Metallic:F2}  "
                                + $"Roughness: {_spheres[idx].MaterialProperties.Properties.Roughness:F2}"
                        );
                        Gui.EndTooltip();
                    }

                    if (col > 0)
                        Gui.SameLine();
                }
            }
        }

        Gui.Spacing();
        Gui.Separator();
        Gui.Spacing();

        // ----------------------------------------------------------------
        // Selected sphere material editor
        // ----------------------------------------------------------------
        if (_selectedIndex < 0 || _selectedIndex >= _spheres.Count)
        {
            Gui.TextDisabled("Click a sphere button above to edit its material.");
            Gui.End();
            return;
        }

        var sphere = _spheres[_selectedIndex];

        Gui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), $"Selected: {sphere.Name}");
        Gui.Separator();
        Gui.Spacing();

        bool changed = false;

        if (Gui.CollapsingHeader("Color", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.Text("Albedo");
            Gui.SetNextItemWidth(-1f);
            if (Gui.ColorEdit3("##Albedo", ref sphere.Albedo))
                changed = true;

            Gui.Spacing();
            Gui.Text("Emissive");
            Gui.SetNextItemWidth(-1f);
            if (Gui.ColorEdit3("##Emissive", ref sphere.Emissive))
                changed = true;

            Gui.Spacing();
            Gui.Text("Ambient");
            Gui.SetNextItemWidth(-1f);
            if (Gui.ColorEdit3("##Ambient", ref sphere.Ambient))
                changed = true;
        }

        Gui.Spacing();

        if (Gui.CollapsingHeader("Surface", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.Text("Metallic");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##Metallic", ref sphere.Metallic, 0f, 1f, "%.3f"))
                changed = true;

            Gui.Text("Roughness");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##Roughness", ref sphere.Roughness, 0.05f, 1f, "%.3f"))
                changed = true;

            Gui.Text("Ambient Occlusion (AO)");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##AO", ref sphere.Ao, 0f, 1f, "%.3f"))
                changed = true;

            Gui.Text("Reflectance (F0)");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##Reflectance", ref sphere.Reflectance, 0f, 1f, "%.3f"))
                changed = true;
            if (Gui.IsItemHovered())
            {
                Gui.BeginTooltip();
                Gui.TextUnformatted(
                    "Fresnel reflectance at normal incidence for dielectric surfaces."
                );
                Gui.TextUnformatted("Typical value: 0.04 for most non-metals.");
                Gui.EndTooltip();
            }

            Gui.Text("Vertex Color Mix");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##VertexColorMix", ref sphere.VertexColorMix, 0f, 1f, "%.3f"))
                changed = true;
            if (Gui.IsItemHovered())
            {
                Gui.BeginTooltip();
                Gui.TextUnformatted("0 = use only albedo color, 1 = use only vertex colors.");
                Gui.EndTooltip();
            }
        }

        Gui.Spacing();

        if (Gui.CollapsingHeader("Clear Coat", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.Text("Clear Coat Strength");
            Gui.SetNextItemWidth(-1f);
            if (
                Gui.SliderFloat("##ClearCoatStrength", ref sphere.ClearCoatStrength, 0f, 1f, "%.3f")
            )
                changed = true;
            if (Gui.IsItemHovered())
            {
                Gui.BeginTooltip();
                Gui.TextUnformatted("0 = no clear coat, 1 = full clear coat layer.");
                Gui.EndTooltip();
            }

            Gui.Text("Clear Coat Roughness");
            Gui.SetNextItemWidth(-1f);
            if (
                Gui.SliderFloat(
                    "##ClearCoatRoughness",
                    ref sphere.ClearCoatRoughness,
                    0f,
                    1f,
                    "%.3f"
                )
            )
                changed = true;
        }

        Gui.Spacing();

        if (Gui.CollapsingHeader("Transparency", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Gui.Text("Opacity");
            Gui.SetNextItemWidth(-1f);
            if (Gui.SliderFloat("##Opacity", ref sphere.Opacity, 0f, 1f, "%.3f"))
                changed = true;
        }

        Gui.Spacing();
        Gui.Separator();
        Gui.Spacing();

        if (Gui.Button("Reset to Default", new Vector2(-1, 0)))
        {
            float metallic = (_selectedIndex / GridCols) / (float)(GridRows - 1);
            float roughness = MathF.Max(0.05f, (_selectedIndex % GridCols) / (float)(GridCols - 1));

            sphere.Albedo = new Vector3(0.7f, 0.3f, 0.1f);
            sphere.Emissive = Vector3.Zero;
            sphere.Ambient = Vector3.Zero;
            sphere.Metallic = metallic;
            sphere.Roughness = roughness;
            sphere.Ao = 1f;
            sphere.Reflectance = 0.04f;
            sphere.VertexColorMix = 0f;
            sphere.ClearCoatStrength = 0f;
            sphere.ClearCoatRoughness = 0f;
            sphere.Opacity = 1f;
            changed = true;
        }

        if (changed)
            sphere.PushToMaterial();

        Gui.End();
    }

    private void Draw3DViewport(TextureHandle offscreenTex, Vector2 pos, Vector2 size)
    {
        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        Gui.SetNextWindowPos(pos, ImGuiCond.Always);
        Gui.SetNextWindowSize(size, ImGuiCond.Always);
        Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin("##Viewport", flags);
        Gui.PopStyleVar();

        var contentSize = Gui.GetContentRegionAvail();
        if (contentSize.X > 0 && contentSize.Y > 0)
        {
            _viewportSize = new HelixToolkit.Nex.Maths.Size((int)contentSize.X, (int)contentSize.Y);
            var canvasPos = Gui.GetCursorScreenPos();
            Gui.Image((nint)offscreenTex.Index, contentSize, Vector2.Zero, Vector2.One);

            bool hovered = Gui.IsItemHovered();
            if (hovered)
            {
                var mousePos = Gui.GetMousePos();
                var rel = new Vector2(mousePos.X - canvasPos.X, mousePos.Y - canvasPos.Y);
                if (Gui.IsMouseClicked(ImGuiMouseButton.Left))
                    OnViewportMouseDown(0, rel.X, rel.Y);
                if (Gui.IsMouseClicked(ImGuiMouseButton.Right))
                    OnViewportMouseDown(1, rel.X, rel.Y);
                if (Gui.IsMouseClicked(ImGuiMouseButton.Middle))
                    OnViewportMouseDown(2, rel.X, rel.Y);

                OnViewportMouseMove(rel.X, rel.Y);

                var io = Gui.GetIO();
                if (MathF.Abs(io.MouseWheel) > 0.001f)
                    OnViewportMouseWheel(io.MouseWheel);
            }

            if (Gui.IsMouseReleased(ImGuiMouseButton.Right))
                OnViewportMouseUp(1);
            if (Gui.IsMouseReleased(ImGuiMouseButton.Middle))
                OnViewportMouseUp(2);
        }

        Gui.End();
    }
}
