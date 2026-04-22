using System.Numerics;
using HelixToolkit.Nex.Shaders;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace TextureTest;

/// <summary>
/// ImGui drawing methods for the PBR texture showcase demo.
/// </summary>
internal sealed partial class TextureDemo
{
    private const float ControlPanelWidth = 340f;

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

    // -------------------------------------------------------------------------
    // Menu bar
    // -------------------------------------------------------------------------

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
            Gui.EndMainMenuBar();
        }
        return height;
    }

    // -------------------------------------------------------------------------
    // Control panel
    // -------------------------------------------------------------------------

    private void DrawControlPanel(Vector2 pos, Vector2 size)
    {
        Gui.SetNextWindowPos(pos, ImGuiCond.Always);
        Gui.SetNextWindowSize(size, ImGuiCond.Always);

        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (!Gui.Begin("Controls##Panel", flags))
        {
            Gui.End();
            return;
        }

        Gui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1f), "PBR Texture Showcase");
        Gui.Separator();
        Gui.Spacing();

        DrawTextureSetSection();
        Gui.Spacing();
        DrawMaterialSection();
        Gui.Spacing();
        DrawAnimationSection();
        Gui.Spacing();
        DrawPostEffectsSection();

        Gui.End();
    }

    // -------------------------------------------------------------------------
    // Texture set selector
    // -------------------------------------------------------------------------

    private void DrawTextureSetSection()
    {
        if (!Gui.CollapsingHeader("Texture Set", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // Build the combo label string once
        var desc = TextureSets[ActiveSetIndex];

        Gui.Text("Active Set:");
        Gui.SetNextItemWidth(-1f);
        if (Gui.BeginCombo("##TextureSet", desc.DisplayName))
        {
            for (int i = 0; i < TextureSets.Length; i++)
            {
                bool selected = i == ActiveSetIndex;
                if (Gui.Selectable(TextureSets[i].DisplayName, selected))
                    SwitchToTextureSet(i);
                if (selected)
                    Gui.SetItemDefaultFocus();
            }
            Gui.EndCombo();
        }

        Gui.Spacing();

        // Show per-slot status for the active set
        var loaded = _loadedSets[ActiveSetIndex];
        DrawSlotRow("Albedo", desc.AlbedoFile, loaded?.Albedo.Valid ?? false);
        DrawSlotRow("Normal", desc.NormalFile, loaded?.Normal.Valid ?? false);
        DrawSlotRow(
            "Metallic-Roughness",
            desc.MetallicRoughnessFile ?? BuildMrLabel(desc),
            loaded?.MetallicRoughness.Valid ?? false
        );
        DrawSlotRow("AO", desc.AoFile, loaded?.Ao.Valid ?? false);
    }

    private static string BuildMrLabel(TextureSetDesc desc)
    {
        if (desc.MetallicFile is null && desc.RoughnessFile is null)
            return "(none)";
        var parts = new List<string>();
        if (desc.MetallicFile is not null)
            parts.Add(Path.GetFileName(desc.MetallicFile));
        if (desc.RoughnessFile is not null)
            parts.Add(
                Path.GetFileName(desc.RoughnessFile)
                    + (desc.RoughnessIsGloss ? " [gloss→rough]" : "")
            );
        return string.Join(" + ", parts) + " (combined)";
    }

    private static void DrawSlotRow(string slot, string? file, bool loaded)
    {
        if (file is null)
        {
            Gui.TextDisabled($"  {slot}: —");
            return;
        }

        var statusColor = loaded
            ? new Vector4(0.3f, 1.0f, 0.4f, 1f)
            : new Vector4(1.0f, 0.4f, 0.3f, 1f);

        Gui.TextColored(statusColor, loaded ? "●" : "✗");
        Gui.SameLine();
        Gui.Text(slot);
        if (Gui.IsItemHovered())
        {
            Gui.BeginTooltip();
            Gui.TextUnformatted(file);
            Gui.EndTooltip();
        }
        Gui.TextDisabled($"    {Path.GetFileName(file)}");
    }

    // -------------------------------------------------------------------------
    // Material scalars
    // -------------------------------------------------------------------------

    private void DrawMaterialSection()
    {
        if (!Gui.CollapsingHeader("Material", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (_material is null)
        {
            Gui.TextDisabled("No material loaded.");
            return;
        }

        bool changed = false;

        Gui.Text("Albedo Tint");
        Gui.SetNextItemWidth(-1f);
        if (Gui.ColorEdit3("##AlbedoTint", ref AlbedoTint))
            changed = true;

        Gui.Spacing();

        Gui.Text("Metallic");
        Gui.SetNextItemWidth(-1f);
        if (Gui.SliderFloat("##Metallic", ref Metallic, 0f, 1f, "%.3f"))
            changed = true;
        if (Gui.IsItemHovered())
        {
            Gui.BeginTooltip();
            Gui.TextUnformatted("Scalar multiplier on top of the metallic texture channel.");
            Gui.EndTooltip();
        }

        Gui.Text("Roughness");
        Gui.SetNextItemWidth(-1f);
        if (Gui.SliderFloat("##Roughness", ref Roughness, 0.02f, 1f, "%.3f"))
            changed = true;
        if (Gui.IsItemHovered())
        {
            Gui.BeginTooltip();
            Gui.TextUnformatted("Scalar multiplier on top of the roughness texture channel.");
            Gui.EndTooltip();
        }

        Gui.Text("Ambient Occlusion");
        Gui.SetNextItemWidth(-1f);
        if (Gui.SliderFloat("##AO", ref Ao, 0f, 1f, "%.3f"))
            changed = true;

        Gui.Text("Opacity");
        Gui.SetNextItemWidth(-1f);
        if (Gui.SliderFloat("##Opacity", ref Opacity, 0f, 1f, "%.3f"))
            changed = true;

        Gui.Spacing();
        Gui.Separator();
        Gui.Spacing();

        if (Gui.Button("Reset to Set Defaults", new Vector2(-1f, 0f)))
        {
            var d = TextureSets[ActiveSetIndex];
            AlbedoTint = Vector3.One;
            Metallic = d.DefaultMetallic;
            Roughness = d.DefaultRoughness;
            Ao = d.DefaultAo;
            Opacity = 1.0f;
            changed = true;
        }

        if (changed)
            PushMaterialChanges();
    }

    private void PushMaterialChanges()
    {
        if (_material is null)
            return;

        _material.Properties.Albedo = AlbedoTint;
        _material.Properties.Metallic = Metallic;
        _material.Properties.Roughness = Roughness;
        _material.Properties.Ao = Ao;
        _material.Properties.Opacity = Opacity;
        _material.NotifyUpdated();
    }

    // -------------------------------------------------------------------------
    // Animation
    // -------------------------------------------------------------------------

    private void DrawAnimationSection()
    {
        if (!Gui.CollapsingHeader("Animation", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        Gui.Checkbox("Auto-Rotate", ref AutoRotate);
        if (!AutoRotate)
        {
            Gui.SameLine();
            Gui.TextDisabled("(right-drag to orbit)");
        }
        Gui.Text($"Rotation: {RotationAngle:F1}°");
    }

    // -------------------------------------------------------------------------
    // Post effects
    // -------------------------------------------------------------------------

    private void DrawPostEffectsSection()
    {
        if (!Gui.CollapsingHeader("Post Effects"))
            return;

        bool fxaaEnabled = Fxaa.Enabled;
        if (Gui.Checkbox("FXAA", ref fxaaEnabled))
            Fxaa.Enabled = fxaaEnabled;

        bool tmEnabled = ToneMapping.Enabled;
        if (Gui.Checkbox("Tone Mapping", ref tmEnabled))
            ToneMapping.Enabled = tmEnabled;

        if (ToneMapping.Enabled)
        {
            Gui.Indent();
            int tmMode = (int)ToneMapping.Mode;
            Gui.SetNextItemWidth(-1f);
            if (Gui.Combo("Mode##TM", ref tmMode, "ACES Film\0Reinhard\0Uncharted 2\0"))
                ToneMapping.Mode = (ToneMappingMode)tmMode;
            Gui.Unindent();
        }

        bool fpsEnabled = ShowFPS.Enabled;
        if (Gui.Checkbox("Show FPS", ref fpsEnabled))
            ShowFPS.Enabled = fpsEnabled;
    }

    // -------------------------------------------------------------------------
    // 3D viewport
    // -------------------------------------------------------------------------

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
