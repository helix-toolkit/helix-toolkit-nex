using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using ImGuiNET;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using static HelixToolkit.Nex.Rendering.PostEffects.WireframePostEffect;
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
            Gui.EndMainMenuBar();
        }
    }

    /// <summary>
    /// Draws the full three-column editor layout:
    ///   [Scene panel] | [3D Viewport] | [Properties panel (top) + Post Effects panel (mid) + Camera panel (bottom)]
    /// All panels are anchored below the main menu bar and sized to fill the display.
    /// </summary>
    private void DrawLayout(TextureHandle offscreenTex, TextureHandle cullingTex, float displayWidth, float displayHeight)
    {
        // Menu bar height is reported by ImGui after BeginMainMenuBar.
        float menuBarHeight = Gui.GetFrameHeight();
        float panelY = menuBarHeight;
        float panelHeight = displayHeight - menuBarHeight;

        float viewportWidth = displayWidth - ScenePanelWidth - PropertiesPanelWidth;

        float thirdHeight = panelHeight / 3f;

        DrawScenePanel(new Vector2(0f, panelY), new Vector2(ScenePanelWidth, panelHeight * 3 / 5f));
        DrawGlobalRenderSettings(
            new Vector2(0f, panelY + panelHeight * 3 / 5f),
            new Vector2(ScenePanelWidth, panelHeight * 2 / 5f)
        );

        // Keep the main viewport's controller in sync with the active controller, which can
        // change at runtime when the camera mode is switched.
        if (_viewport is not null)
            _viewport.CameraController = _activeController;

        _viewport?.Draw(
            offscreenTex,
            new Vector2(ScenePanelWidth, panelY),
            new Vector2(viewportWidth / 2, panelHeight)
        );

        _cullViewport?.Draw(
            cullingTex,
            new Vector2(ScenePanelWidth + viewportWidth / 2, panelY),
            new Vector2(viewportWidth / 2, panelHeight)
        );



        DrawPropertiesPanel(
            new Vector2(ScenePanelWidth + viewportWidth, panelY),
            new Vector2(PropertiesPanelWidth, thirdHeight)
        );
        DrawPostEffectsPanel(
            new Vector2(ScenePanelWidth + viewportWidth, panelY + thirdHeight),
            new Vector2(PropertiesPanelWidth, thirdHeight)
        );
        DrawCameraPanel(
            new Vector2(ScenePanelWidth + viewportWidth, panelY + thirdHeight * 2f),
            new Vector2(PropertiesPanelWidth, thirdHeight)
        );
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
            if (node.Entity.Has<MeshDrawInfo>())
            {
                Gui.Text($"Mesh: {node.Entity.Get<MeshDrawInfo>()}");
            }
            if (node.Entity.Has<RangeLightInfo>())
            {
                var light = node.Entity.Get<RangeLightInfo>();
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
        if (_selectedEntity.TryGet<MeshDrawInfo>(out var mesh))
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
        if (_selectedEntity.TryGet<RangeLightInfo>(out var light))
        {
            if (light is not null && Gui.CollapsingHeader("Range Light"))
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
        if (_selectedEntity.TryGet<DirectionalLightInfo>(out var dirLight))
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
        if (_fxaa is not null && Gui.CollapsingHeader("FXAA"))
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
        if (_smaa is not null && Gui.CollapsingHeader("SMAA"))
        {
            bool smaaEnabled = _smaa.Enabled;
            if (Gui.Checkbox("Enabled##SMAA", ref smaaEnabled))
                _smaa.Enabled = smaaEnabled;

            int smaaQuality = (int)_smaa.Quality;
            if (Gui.Combo("Quality##SMAA", ref smaaQuality, "Low\0Medium\0High\0Ultra\0"))
                _smaa.Quality = (SMAAQuality)smaaQuality;

            float edge = _smaa.EdgeThreshold;
            if (Gui.SliderFloat("Edge Threshold##SMAA", ref edge, 0.001f, 0.5f))
                _smaa.EdgeThreshold = edge;
        }

        // --- Bloom ---
        if (_bloom is not null && Gui.CollapsingHeader("Bloom"))
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
            var toneMappingNode = _engine!.GetRenderNode<ToneMappingNode>()!;
            bool tmEnabled = toneMappingNode.Enabled;
            if (Gui.Checkbox("Enabled##ToneMapping", ref tmEnabled))
                toneMappingNode.Enabled = tmEnabled;

            int tmMode = (int)toneMappingNode.Mode;
            if (Gui.Combo("Mode##ToneMapping", ref tmMode, "ACES Film\0Reinhard\0Uncharted 2\0"))
                toneMappingNode.Mode = (ToneMappingMode)tmMode;
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
        }

        // --- Show FPS ---
        if (_showFPS is not null && Gui.CollapsingHeader("Show FPS"))
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
            _selectedEntity.Remove<BorderHighlightOverlay>();
            _selectedEntity.Remove<WireframeOverlay>();
        }

        _selectedEntity = entity;

        // Apply highlight to new selection
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightOverlay.Default);
            _selectedEntity.Set(new WireframeOverlay() { Color = new Color4(1f, 0f, 0f, 1f) });
        }
    }

    private void DrawCameraPanel(Vector2 pos, Vector2 size)
    {
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Camera##Panel", windowFlags);

        Gui.Text("Camera Controller");
        Gui.Separator();

        // --- Controller type selector ---
        int mode = (int)CameraMode;
        if (Gui.Combo("Mode##Camera", ref mode, "Orbit\0Turntable\0First Person\0"))
        {
            CameraMode = (CameraControllerMode)mode;
        }

        Gui.Spacing();

        // --- Camera position / target display ---
        var camPos = _camera.Position;
        var camTarget = _camera.Target;
        Gui.Text($"Position: ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})");
        Gui.Text($"Target:   ({camTarget.X:F1}, {camTarget.Y:F1}, {camTarget.Z:F1})");

        Gui.Separator();

        // --- Per-mode settings ---
        switch (CameraMode)
        {
            case CameraControllerMode.Orbit:
                DrawOrbitSettings();
                break;
            case CameraControllerMode.Turntable:
                DrawTurntableSettings();
                break;
            case CameraControllerMode.FirstPerson:
                DrawFirstPersonSettings();
                break;
        }

        Gui.Spacing();
        if (Gui.Button("Reset Camera"))
        {
            _activeController?.Reset();
        }

        Gui.End();
    }

    private void DrawOrbitSettings()
    {
        if (_orbitController is null)
            return;

        float rotSens = _orbitController.RotationSensitivity;
        if (Gui.SliderFloat("Rotation Sens##Orbit", ref rotSens, 0.001f, 0.02f))
            _orbitController.RotationSensitivity = rotSens;

        float panSens = _orbitController.PanSensitivity;
        if (Gui.SliderFloat("Pan Sens##Orbit", ref panSens, 0.001f, 0.05f))
            _orbitController.PanSensitivity = panSens;

        float zoomSens = _orbitController.ZoomSensitivity;
        if (Gui.SliderFloat("Zoom Sens##Orbit", ref zoomSens, 0.01f, 0.5f))
            _orbitController.ZoomSensitivity = zoomSens;

        bool invertY = _orbitController.InvertY;
        if (Gui.Checkbox("Invert Y##Orbit", ref invertY))
            _orbitController.InvertY = invertY;
    }

    private void DrawTurntableSettings()
    {
        if (_turntableController is null)
            return;

        float rotSens = _turntableController.RotationSensitivity;
        if (Gui.SliderFloat("Rotation Sens##Turntable", ref rotSens, 0.001f, 0.02f))
            _turntableController.RotationSensitivity = rotSens;

        float panSens = _turntableController.PanSensitivity;
        if (Gui.SliderFloat("Pan Sens##Turntable", ref panSens, 0.001f, 0.05f))
            _turntableController.PanSensitivity = panSens;

        float zoomSens = _turntableController.ZoomSensitivity;
        if (Gui.SliderFloat("Zoom Sens##Turntable", ref zoomSens, 0.01f, 0.5f))
            _turntableController.ZoomSensitivity = zoomSens;

        float inertia = _turntableController.InertiaDamping;
        if (Gui.SliderFloat("Inertia##Turntable", ref inertia, 0f, 0.98f))
            _turntableController.InertiaDamping = inertia;

        bool invertY = _turntableController.InvertY;
        if (Gui.Checkbox("Invert Y##Turntable", ref invertY))
            _turntableController.InvertY = invertY;
    }

    private void DrawFirstPersonSettings()
    {
        if (_walkaroundController is null)
            return;

        Gui.TextWrapped(
            "Right-drag to look. WASD to move, Space/Ctrl for up/down, Shift to sprint."
        );
        Gui.Spacing();

        float lookSens = _walkaroundController.LookSensitivity;
        if (Gui.SliderFloat("Look Sens##FP", ref lookSens, 0.001f, 0.01f))
            _walkaroundController.LookSensitivity = lookSens;

        float moveSpeed = _walkaroundController.MoveSpeed;
        if (Gui.SliderFloat("Move Speed##FP", ref moveSpeed, 1f, 100f))
            _walkaroundController.MoveSpeed = moveSpeed;

        float sprintMul = _walkaroundController.SprintMultiplier;
        if (Gui.SliderFloat("Sprint Multiplier##FP", ref sprintMul, 1f, 10f))
            _walkaroundController.SprintMultiplier = sprintMul;

        bool invertY = _walkaroundController.InvertY;
        if (Gui.Checkbox("Invert Y##FP", ref invertY))
            _walkaroundController.InvertY = invertY;
    }

    private void DrawGlobalRenderSettings(Vector2 pos, Vector2 size)
    {
        if (_renderContext is null)
        {
            return;
        }
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Global##Render##Settings", windowFlags);
        Gui.Separator();
        bool wireframeModeEnabled = _renderContext.RenderParams.EnableGlobalWireframe;
        if (Gui.Checkbox("Global Wireframe Mode", ref wireframeModeEnabled))
        {
            _renderContext.RenderParams.EnableGlobalWireframe = wireframeModeEnabled;
        }
        Gui.Checkbox("Wireframe effect per instance", ref _perInstance);
        Gui.End();
    }
}
