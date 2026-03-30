using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace ImGuiTest;

/// <summary>
/// Contains all ImGui drawing methods for the editor UI.
/// </summary>
internal partial class Editor
{
    // Width of the scene-tree panel on the left.
    private const float ScenePanelWidth = 260f;

    // Width of the properties panel on the right.
    private const float PropertiesPanelWidth = 300f;

    private void DrawMainMenuBar()
    {
        if (Gui.BeginMainMenuBar())
        {
            if (Gui.BeginMenu("File"))
            {
                Gui.MenuItem("Open Scene...");
                Gui.MenuItem("Save Scene");
                Gui.Separator();
                Gui.MenuItem("Exit");
                Gui.EndMenu();
            }
            if (Gui.BeginMenu("View"))
            {
                Gui.MenuItem("Scene");
                Gui.MenuItem("Properties");
                Gui.MenuItem("Viewport");
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }
    }

    /// <summary>
    /// Draws the full three-column editor layout:
    ///   [Scene panel] | [3D Viewport] | [Properties panel (top) + Post Effects panel (bottom)]
    /// All panels are anchored below the main menu bar and sized to fill the display.
    /// </summary>
    private void DrawLayout(TextureHandle offscreenTex, float displayWidth, float displayHeight)
    {
        // Menu bar height is reported by ImGui after BeginMainMenuBar.
        float menuBarHeight = Gui.GetFrameHeight();
        float panelY = menuBarHeight;
        float panelHeight = displayHeight - menuBarHeight;

        float viewportWidth = displayWidth - ScenePanelWidth - PropertiesPanelWidth;

        float halfHeight = panelHeight / 2f;

        DrawScenePanel(new Vector2(0f, panelY), new Vector2(ScenePanelWidth, panelHeight));
        Draw3DViewport(
            offscreenTex,
            new Vector2(ScenePanelWidth, panelY),
            new Vector2(viewportWidth, panelHeight)
        );
        DrawPropertiesPanel(
            new Vector2(ScenePanelWidth + viewportWidth, panelY),
            new Vector2(PropertiesPanelWidth, halfHeight)
        );
        DrawPostEffectsPanel(
            new Vector2(ScenePanelWidth + viewportWidth, panelY + halfHeight),
            new Vector2(PropertiesPanelWidth, halfHeight)
        );
    }

    private void Draw3DViewport(TextureHandle offscreenTex, Vector2 pos, Vector2 size)
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin("##Viewport", windowFlags);
        Gui.PopStyleVar();

        var contentSize = Gui.GetContentRegionAvail();
        if (contentSize.X > 0 && contentSize.Y > 0)
        {
            // Update the viewport size so the next frame's render graph allocates
            // offscreen textures at this resolution.
            _viewportSize = new Size((int)contentSize.X, (int)contentSize.Y);
            var canvas_pos = Gui.GetCursorScreenPos();
            Gui.Image((nint)offscreenTex.Index, contentSize, new Vector2(0, 0), new Vector2(1, 1));
            if (Gui.IsItemHovered() && Gui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var mouse_pos = Gui.GetMousePos();
                var relative_pos = new Vector2(
                    mouse_pos.X - canvas_pos.X,
                    mouse_pos.Y - canvas_pos.Y
                );
                _logger.LogInformation(
                    "Mouse clicked at viewport coords: {X}, {Y}",
                    relative_pos.X,
                    relative_pos.Y
                );
                Pick((int)relative_pos.X, (int)relative_pos.Y);
            }
        }

        Gui.End();
    }

    private void DrawScenePanel(Vector2 pos, Vector2 size)
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Scene##Panel", windowFlags);

        Gui.Text("Scene Hierarchy");
        Gui.Separator();

        if (_root is not null)
        {
            DrawNodeTree(_root);
        }
        else
        {
            Gui.TextDisabled("No scene loaded.");
        }

        Gui.End();
    }

    private void DrawNodeTree(Node node)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;

        // Highlight the currently selected entity
        if (_selectedEntity.Valid && node.Entity == _selectedEntity)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        // Leaf nodes (no children) get a bullet instead of an arrow
        if (!node.HasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        // Build a display label: prefer name, fall back to entity id
        var label = string.IsNullOrEmpty(node.Name) ? $"Entity {node.Entity.Id}" : node.Name;

        // Dim disabled nodes
        bool dimmed = !node.Enabled;
        if (dimmed)
        {
            Gui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
        }

        bool opened = Gui.TreeNodeEx($"{label}##{node.Entity.Id}", flags);

        if (dimmed)
        {
            Gui.PopStyleColor();
        }

        // Selection on click
        if (Gui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectEntity(node.Entity);
        }

        // Tooltip with extra info on hover
        if (Gui.IsItemHovered())
        {
            Gui.BeginTooltip();
            Gui.Text($"Entity: {node.Entity.Id}  Level: {node.Level}");
            if (node.Entity.Has<MeshComponent>())
            {
                Gui.Text($"Mesh: {node.Entity.Get<MeshComponent>()}");
            }
            if (node.Entity.Has<RangeLightComponent>())
            {
                var light = node.Entity.Get<RangeLightComponent>();
                Gui.Text($"Light: {light.Type}  Range: {light.Range:F1}");
            }
            Gui.EndTooltip();
        }

        // Recurse into children (only when the tree node is opened and has children)
        if (opened && node.HasChildren)
        {
            foreach (var child in node.Children!)
            {
                DrawNodeTree(child);
            }
            Gui.TreePop();
        }
    }

    private void DrawPropertiesPanel(Vector2 pos, Vector2 size)
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Properties##Panel", windowFlags);

        if (!_selectedEntity.Valid)
        {
            Gui.TextDisabled("Select an entity to inspect.");
            Gui.End();
            return;
        }

        Gui.Text($"Entity: {_selectedEntity.Id}");
        Gui.Separator();

        // --- Node info ---
        if (_selectedEntity.TryGet<NodeInfo>(out var info))
        {
            if (Gui.CollapsingHeader("Node Info", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Gui.Text($"Level: {info.Level}");
                Gui.Text($"Enabled: {info.Enabled}");
            }
        }

        // --- Transform ---
        if (_selectedEntity.Has<Transform>())
        {
            if (Gui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ref var transform = ref _selectedEntity.Get<Transform>();
                var pos2 = transform.Translation;
                var scale = transform.Scale;
                Gui.InputFloat3("Position", ref pos2);
                Gui.InputFloat3("Scale", ref scale);
            }
        }

        // --- Mesh component ---
        if (_selectedEntity.TryGet<MeshComponent>(out var mesh))
        {
            if (Gui.CollapsingHeader("Mesh Component"))
            {
                Gui.Text($"Geometry: {mesh.Geometry?.Id}");
                Gui.Text($"Material Type: {mesh.MaterialProperties?.MaterialTypeId}");
                Gui.Text($"Instancing: {(mesh.Instancing is not null ? "Yes" : "No")}");
                Gui.Text($"Cullable: {mesh.Cullable}");
                Gui.Text($"Hitable: {mesh.Hitable}");
            }
        }

        // --- Light component ---
        if (_selectedEntity.TryGet<RangeLightComponent>(out var light))
        {
            if (Gui.CollapsingHeader("Range Light"))
            {
                Gui.Text($"Type: {light.Type}");
                Gui.Text($"Range: {light.Range:F2}");
                Gui.Text($"Intensity: {light.Intensity:F2}");
                var color = light.Color.ToVector3();
                if (Gui.ColorEdit3("Color", ref color))
                {
                    light.Color = color.ToColor4(1);
                    _selectedEntity.Set(light);
                }
            }
        }

        // --- Directional light ---
        if (_selectedEntity.TryGet<DirectionalLightComponent>(out var dirLight))
        {
            if (Gui.CollapsingHeader("Directional Light"))
            {
                if (
                    Gui.SliderFloat(
                        $"Intensity: {dirLight.Intensity:F2}",
                        ref dirLight.Light.Intensity,
                        0,
                        1
                    )
                )
                {
                    _selectedEntity.Set(dirLight);
                }
                var dir = dirLight.Direction;
                Gui.InputFloat3("Direction", ref dir);
                var color = dirLight.Color.ToVector3();
                if (Gui.ColorEdit3("Color", ref color))
                {
                    dirLight.Color = color.ToColor4(1);
                    _selectedEntity.Set(dirLight);
                }
            }
        }

        Gui.End();
    }

    private void DrawPostEffectsPanel(Vector2 pos, Vector2 size)
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Post Effects##Panel", windowFlags);

        Gui.Text("Post Effects");
        Gui.Separator();

        // --- FXAA ---
        if (Gui.CollapsingHeader("FXAA"))
        {
            bool fxaaEnabled = _fxaa.Enabled;
            if (Gui.Checkbox("Enabled##FXAA", ref fxaaEnabled))
                _fxaa.Enabled = fxaaEnabled;

            var mode = (int)_fxaa.DebugMode;
            if (Gui.Combo("Debug Mode##FXAA", ref mode, "Off\0EdgeMask\0BlendHeatMap\0"))
                _fxaa.DebugMode = (FxaaDebugMode)mode;

            int fxaaQuality = (int)_fxaa.Quality;
            if (Gui.Combo("Quality##FXAA", ref fxaaQuality, "Low\0Medium\0High\0Ultra\0"))
                _fxaa.Quality = (FxaaQuality)fxaaQuality;

            float contrast = _fxaa.ContrastThreshold;
            if (Gui.SliderFloat("Contrast Threshold##FXAA", ref contrast, 0.001f, 0.5f))
                _fxaa.ContrastThreshold = contrast;

            float relative = _fxaa.RelativeThreshold;
            if (Gui.SliderFloat("Relative Threshold##FXAA", ref relative, 0.001f, 0.5f))
                _fxaa.RelativeThreshold = relative;

            float subpixel = _fxaa.SubpixelBlending;
            if (Gui.SliderFloat("Subpixel Blending##FXAA", ref subpixel, 0f, 1f))
                _fxaa.SubpixelBlending = subpixel;
        }

        // --- SMAA ---
        if (Gui.CollapsingHeader("SMAA"))
        {
            bool smaaEnabled = _smaa.Enabled;
            if (Gui.Checkbox("Enabled##SMAA", ref smaaEnabled))
                _smaa.Enabled = smaaEnabled;

            int smaaQuality = (int)_smaa.Quality;
            if (Gui.Combo("Quality##SMAA", ref smaaQuality, "Low\0Medium\0High\0Ultra\0"))
                _smaa.Quality = (SmaaQuality)smaaQuality;

            float edge = _smaa.EdgeThreshold;
            if (Gui.SliderFloat("Edge Threshold##SMAA", ref edge, 0.001f, 0.5f))
                _smaa.EdgeThreshold = edge;
        }

        // --- Bloom ---
        if (Gui.CollapsingHeader("Bloom"))
        {
            bool bloomEnabled = _bloom.Enabled;
            if (Gui.Checkbox("Enabled##Bloom", ref bloomEnabled))
                _bloom.Enabled = bloomEnabled;

            float threshold = _bloom.Threshold;
            if (Gui.SliderFloat("Threshold##Bloom", ref threshold, 0f, 2f))
                _bloom.Threshold = threshold;

            float intensity = _bloom.Intensity;
            if (Gui.SliderFloat("Intensity##Bloom", ref intensity, 0f, 10f))
                _bloom.Intensity = intensity;

            int blurPasses = _bloom.BlurPasses;
            if (Gui.SliderInt("Blur Passes##Bloom", ref blurPasses, 1, 8))
                _bloom.BlurPasses = blurPasses;

            int downsample = _bloom.DownsampleFactor;
            if (
                Gui.Combo(
                    "Downsample##Bloom",
                    ref downsample,
                    "1x\0Half (2)\0Quarter (4)\0Eighth (8)\0",
                    4
                )
            )
                _bloom.DownsampleFactor =
                    downsample == 0 ? 1
                    : downsample == 1 ? 2
                    : downsample == 2 ? 4
                    : 8;
        }

        // --- Tone Mapping ---
        if (Gui.CollapsingHeader("Tone Mapping"))
        {
            bool tmEnabled = _toneMapping.Enabled;
            if (Gui.Checkbox("Enabled##ToneMapping", ref tmEnabled))
                _toneMapping.Enabled = tmEnabled;

            int tmMode = (int)_toneMapping.Mode;
            if (Gui.Combo("Mode##ToneMapping", ref tmMode, "ACES Film\0Reinhard\0Uncharted 2\0"))
                _toneMapping.Mode = (ToneMappingMode)tmMode;
        }

        // --- Border Highlight ---
        if (Gui.CollapsingHeader("Border Highlight"))
        {
            bool bhEnabled = _borderHighlight.Enabled;
            if (Gui.Checkbox("Enabled##BorderHighlight", ref bhEnabled))
                _borderHighlight.Enabled = bhEnabled;
        }

        // --- Wireframe ---
        if (Gui.CollapsingHeader("Wireframe"))
        {
            bool wfEnabled = _wireframe.Enabled;
            if (Gui.Checkbox("Enabled##Wireframe", ref wfEnabled))
                _wireframe.Enabled = wfEnabled;

            float depthBiasConst = _wireframe.DepthBiasConstant;
            if (Gui.SliderFloat("Depth Bias Const##WF", ref depthBiasConst, -10f, 10f))
                _wireframe.DepthBiasConstant = depthBiasConst;

            float depthBiasSlope = _wireframe.DepthBiasSlope;
            if (Gui.SliderFloat("Depth Bias Slope##WF", ref depthBiasSlope, -10f, 10f))
                _wireframe.DepthBiasSlope = depthBiasSlope;
        }

        // --- Show FPS ---
        if (Gui.CollapsingHeader("Show FPS"))
        {
            bool fpsEnabled = _showFPS.Enabled;
            if (Gui.Checkbox("Enabled##ShowFPS", ref fpsEnabled))
                _showFPS.Enabled = fpsEnabled;

            float fpsScale = _showFPS.Scale;
            if (Gui.SliderFloat("Scale##ShowFPS", ref fpsScale, 0.01f, 0.2f))
                _showFPS.Scale = fpsScale;
        }

        Gui.End();
    }

    private void SelectEntity(Entity entity)
    {
        // Deselect previous
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Remove<BorderHighlightComponent>();
            _selectedEntity.Remove<WireframeComponent>();
        }

        _selectedEntity = entity;

        // Apply highlight to new selection
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightComponent.Default);
            _selectedEntity.Set(new WireframeComponent() { Color = new Color4(1f, 0f, 0f, 1f) });
        }
    }
}
