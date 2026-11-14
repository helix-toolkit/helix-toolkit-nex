using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using WorldDataProvider = HelixToolkit.Nex.Engine.WorldDataProvider;

namespace HelixToolkit.Nex.Sample.GltfImporter;

/// <summary>
/// Displays properties of the currently selected entity.
/// Shows transform (position, rotation, scale) for all entities,
/// plus mesh info (vertex count, topology, material) for mesh entities.
/// </summary>
internal class PropertiesPanel
{
    private readonly SelectionManager _selectionManager;
    private readonly WorldDataProvider _worldDataProvider;
    private readonly RenderContext _renderContext;
    private readonly GltfImporterApp _app;

    public PropertiesPanel(
        SelectionManager selectionManager,
        WorldDataProvider worldDataProvider,
        RenderContext context,
        GltfImporterApp app
    )
    {
        _selectionManager =
            selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
        _worldDataProvider =
            worldDataProvider ?? throw new ArgumentNullException(nameof(worldDataProvider));
        _renderContext = context ?? throw new ArgumentNullException(nameof(context));
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <summary>
    /// Draws the properties panel. Shows "No entity selected" when nothing
    /// is selected. Updates content on the same frame as selection changes.
    /// Numeric values are displayed to 3 decimal places.
    /// </summary>
    public void Draw(Vector2 position, Vector2 size)
    {
        Gui.SetNextWindowPos(position);
        Gui.SetNextWindowSize(size);
        if (Gui.Begin("Settings", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            if (Gui.BeginChild("SettingsContent"))
            {
                Gui.Text("Importer Settings");
                var mode = _app.ImportConfig.DefaultShadingMode;
                if (Gui.BeginCombo("Default Shading Mode", mode.ToString()))
                {
                    foreach (var value in Enum.GetValues<PBRShadingMode>())
                    {
                        var isSelected = value == mode;
                        if (Gui.Selectable(value.ToString(), isSelected))
                        {
                            _app.ImportConfig.DefaultShadingMode = value;
                            _app.UpdateShadingMode();
                        }
                        if (isSelected)
                        {
                            Gui.SetItemDefaultFocus();
                        }
                    }
                    Gui.EndCombo();
                }
                var winding = _app.ImportConfig.WindingOrder;
                if (Gui.BeginCombo("Winding Order", winding.ToString()))
                {
                    foreach (var value in Enum.GetValues<WindingMode>())
                    {
                        var isSelected = value == winding;
                        if (Gui.Selectable(value.ToString(), isSelected))
                        {
                            _app.ImportConfig.WindingOrder = value;
                        }
                        if (isSelected)
                        {
                            Gui.SetItemDefaultFocus();
                        }
                    }
                    Gui.EndCombo();
                }
                Gui.TextDisabled("Applies to the next file loaded.");
                Gui.Spacing();
                var enableSSAO = _app.EnableSSAO;
                if (Gui.Checkbox("Enable SSAO", ref enableSSAO))
                {
                    _app.EnableSSAO = enableSSAO;
                }
                Gui.Spacing();
                Gui.Text("Directional Light");
                var light = _app.DirectionalLight;
                var intensity = light.Intensity;
                if (Gui.SliderFloat("Intensity", ref intensity, 0.0f, 10.0f))
                {
                    light.Intensity = intensity;
                }
                var color = light.Color.ToVector3();
                if (Gui.ColorEdit3("Color", ref color))
                {
                    light.Color = color.ToColor4(1);
                }
                Gui.Spacing();
                var wireframe = _renderContext.RenderParams.EnableGlobalWireframe;
                Gui.Checkbox("Wireframe Mode", ref wireframe);
                _renderContext.RenderParams.EnableGlobalWireframe = wireframe;
                if (_app.HasSkeleton)
                {
                    var showSkeleton = _app.ShowSkeleton;
                    if (Gui.Checkbox("Show Skeleton", ref showSkeleton))
                    {
                        _app.ShowSkeleton = showSkeleton;
                    }
                    var skeleton = _app.SkeletonEffect;
                    Gui.BeginDisabled(!showSkeleton);
                    if (Gui.BeginCombo("Bone Style", skeleton.Style.ToString()))
                    {
                        foreach (var value in Enum.GetValues<BoneStyle>())
                        {
                            var isSelected = value == skeleton.Style;
                            if (Gui.Selectable(value.ToString(), isSelected))
                            {
                                skeleton.Style = value;
                            }
                            if (isSelected)
                            {
                                Gui.SetItemDefaultFocus();
                            }
                        }
                        Gui.EndCombo();
                    }
                    var headSize = skeleton.HeadSize;
                    if (Gui.SliderFloat("Bone Head Size", ref headSize, 0.05f, 0.5f))
                    {
                        skeleton.HeadSize = headSize;
                    }
                    var depthTest = skeleton.UseDepthTest;
                    if (Gui.Checkbox("Bone Depth Test", ref depthTest))
                    {
                        skeleton.UseDepthTest = depthTest;
                    }
                    Gui.EndDisabled();
                }
                if (_app.HasMorphTargets)
                {
                    var showMorph = _app.ShowMorphDisplacement;
                    if (Gui.Checkbox("Show Morph Displacement", ref showMorph))
                    {
                        _app.ShowMorphDisplacement = showMorph;
                    }
                    var morph = _app.MorphEffect;
                    Gui.BeginDisabled(!showMorph);
                    var lengthScale = morph.LengthScale;
                    if (Gui.SliderFloat("Displacement Scale", ref lengthScale, 0.1f, 5f))
                    {
                        morph.LengthScale = lengthScale;
                    }
                    var morphDepthTest = morph.UseDepthTest;
                    if (Gui.Checkbox("Displacement Depth Test", ref morphDepthTest))
                    {
                        morph.UseDepthTest = morphDepthTest;
                    }
                    Gui.EndDisabled();
                }

                Gui.Spacing();
                Gui.Separator();
                Gui.Text("Environment Map");
                var env = _renderContext.EnvironmentMap;
                if (!env.HasValidTexture)
                {
                    Gui.TextDisabled("No environment map loaded");
                }
                var envEnabled = env.Enabled;
                if (Gui.Checkbox("Enable Environment Map", ref envEnabled))
                {
                    env.Enabled = envEnabled;
                }
                // Intensity/rotation/blur and the cubemap toggle also drive the PBR
                // reflections; gate them on a valid cubemap being loaded.
                Gui.BeginDisabled(!env.HasValidTexture);
                var envIntensity = env.Intensity;
                if (Gui.SliderFloat("Env Intensity", ref envIntensity, 0.0f, 5.0f))
                {
                    env.Intensity = envIntensity;
                }
                var envRotationDeg = env.RotationY * (180f / MathF.PI);
                if (Gui.SliderFloat("Env Rotation", ref envRotationDeg, -180f, 180f))
                {
                    env.RotationY = envRotationDeg * (MathF.PI / 180f);
                }
                var envBlur = env.Blur;
                if (Gui.SliderFloat("Env Blur", ref envBlur, 0.0f, 8.0f))
                {
                    env.Blur = envBlur;
                }
                var renderCubeMap = env.RenderCubeMap;
                if (Gui.Checkbox("PBR Cubemap Reflections", ref renderCubeMap))
                {
                    env.RenderCubeMap = renderCubeMap;
                }
                Gui.EndDisabled();

                Gui.Spacing();
                Gui.Separator();
                Gui.Text("Sky Atmosphere");
                var sky = _renderContext.SkyAtmosphere;
                var skyEnabled = sky.Enabled;
                if (Gui.Checkbox("Enable Sky Atmosphere", ref skyEnabled))
                {
                    sky.Enabled = skyEnabled;
                }
                Gui.BeginDisabled(!sky.Enabled);
                var sunElevation = _app.SunElevationDeg;
                if (Gui.SliderFloat("Sun Elevation", ref sunElevation, -10f, 90f))
                {
                    _app.SunElevationDeg = sunElevation;
                }
                var sunAzimuth = _app.SunAzimuthDeg;
                if (Gui.SliderFloat("Sun Azimuth", ref sunAzimuth, -180f, 180f))
                {
                    _app.SunAzimuthDeg = sunAzimuth;
                }
                var bindIntensity = sky.SunIntensityFromDirectionalLight;
                if (Gui.Checkbox("Sun Follows Light Intensity", ref bindIntensity))
                {
                    sky.SunIntensityFromDirectionalLight = bindIntensity;
                }
                if (sky.SunIntensityFromDirectionalLight)
                {
                    var intensityScale = sky.DirectionalLightIntensityScale;
                    if (Gui.SliderFloat("Light To Sun Scale", ref intensityScale, 1f, 100f))
                    {
                        sky.DirectionalLightIntensityScale = intensityScale;
                    }
                }
                else
                {
                    var sunIntensity = sky.SunIntensity;
                    if (Gui.SliderFloat("Sun Intensity", ref sunIntensity, 1f, 60f))
                    {
                        sky.SunIntensity = sunIntensity;
                    }
                }
                var bindColor = sky.SunColorFromDirectionalLight;
                if (Gui.Checkbox("Sun Follows Light Color", ref bindColor))
                {
                    sky.SunColorFromDirectionalLight = bindColor;
                }
                if (!sky.SunColorFromDirectionalLight)
                {
                    var sunColor = sky.SunColor;
                    if (Gui.ColorEdit3("Sun Color", ref sunColor))
                    {
                        sky.SunColor = sunColor;
                    }
                }
                var groundColor = sky.GroundColor;
                if (Gui.ColorEdit3("Ground Color", ref groundColor))
                {
                    sky.GroundColor = groundColor;
                }
                var groundBlendDeg = sky.GroundBlendAngle * (180f / MathF.PI);
                if (Gui.SliderFloat("Ground Blend", ref groundBlendDeg, 0f, 30f, "%.1f deg"))
                {
                    sky.GroundBlendAngle = groundBlendDeg * (MathF.PI / 180f);
                }
                var sunDiscSize = sky.SunDiscSize;
                if (Gui.SliderFloat("Sun Disc Size", ref sunDiscSize, 0.1f, 5f))
                {
                    sky.SunDiscSize = sunDiscSize;
                }
                var hazeScale = sky.AerialPerspectiveScale;
                if (
                    Gui.SliderFloat(
                        "Haze Scale",
                        ref hazeScale,
                        1f,
                        1000f,
                        "%.0f",
                        ImGuiSliderFlags.Logarithmic
                    )
                )
                {
                    sky.AerialPerspectiveScale = hazeScale;
                }
                var mieG = sky.MieG;
                if (Gui.SliderFloat("Mie G", ref mieG, 0f, 0.99f))
                {
                    sky.MieG = mieG;
                }
                var mieEnabled = sky.MieEnabled;
                if (Gui.Checkbox("Mie Scattering", ref mieEnabled))
                {
                    sky.MieEnabled = mieEnabled;
                }
                var ozoneEnabled = sky.OzoneEnabled;
                if (Gui.Checkbox("Ozone Absorption", ref ozoneEnabled))
                {
                    sky.OzoneEnabled = ozoneEnabled;
                }
                var envAsSpace = sky.ShowEnvironmentMapAsSpace;
                if (Gui.Checkbox("Env Map Behind Sky", ref envAsSpace))
                {
                    sky.ShowEnvironmentMapAsSpace = envAsSpace;
                }
                Gui.EndDisabled();
            }
            Gui.EndChild();
            if (Gui.BeginChild("Properties"))
            {
                if (!_selectionManager.HasSelection)
                {
                    Gui.TextUnformatted("No entity selected");
                }
                else
                {
                    DrawEntityProperties();
                }
            }
            Gui.EndChild();
        }
        Gui.End();
    }

    private void DrawEntityProperties()
    {
        var entity = _selectionManager.SelectedEntity;

        // --- Transform ---
        if (entity.Has<Transform>())
        {
            ref var transform = ref entity.Get<Transform>();

            if (Gui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Gui.Text("Position");
                Gui.Text($"  X: {transform.Translation.X:F3}");
                Gui.Text($"  Y: {transform.Translation.Y:F3}");
                Gui.Text($"  Z: {transform.Translation.Z:F3}");

                Gui.Separator();
                Gui.Text("Rotation");
                Gui.Text($"  X: {transform.Rotation.X:F3}");
                Gui.Text($"  Y: {transform.Rotation.Y:F3}");
                Gui.Text($"  Z: {transform.Rotation.Z:F3}");
                Gui.Text($"  W: {transform.Rotation.W:F3}");

                Gui.Separator();
                Gui.Text("Scale");
                Gui.Text($"  X: {transform.Scale.X:F3}");
                Gui.Text($"  Y: {transform.Scale.Y:F3}");
                Gui.Text($"  Z: {transform.Scale.Z:F3}");
            }
        }

        // --- Mesh component ---
        if (entity.TryGet<MeshDrawInfo>(out var mesh))
        {
            if (mesh.Geometry is not null && mesh.MaterialProperties is not null)
            {
                if (Gui.CollapsingHeader("Mesh", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    Gui.Text($"Vertex Count: {mesh.Geometry.Vertices.Count}");
                    Gui.Text($"Topology: {mesh.Geometry.Topology}");

                    var materialName =
                        PBRMaterialTypeRegistry.GetTypeName(mesh.MaterialProperties.MaterialTypeId)
                        ?? "Unknown";
                    Gui.Text($"Material: {materialName}");
                }
            }
            if (mesh.Morphing is { } morphing && mesh.Geometry is not null)
            {
                DrawMorphWeights(morphing, mesh.Geometry);
            }
        }
    }

    /// <summary>
    /// Draws a slider per morph target. Weights are shared by every primitive of the mesh, and a
    /// playing weight animation overrides the sliders.
    /// </summary>
    private static void DrawMorphWeights(Morphing morphing, Geometries.Geometry geometry)
    {
        if (!Gui.CollapsingHeader("Morph Targets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }
        var weights = morphing.Weights;
        for (var i = 0; i < weights.Count; i++)
        {
            var name =
                i < geometry.MorphTargets.Count && geometry.MorphTargets[i].Name.Length > 0
                    ? geometry.MorphTargets[i].Name
                    : $"Target_{i}";
            var weight = weights[i];
            if (Gui.SliderFloat($"{name}##morph{i}", ref weight, 0f, 1f))
            {
                weights[i] = weight;
            }
        }
        Gui.TextDisabled($"{morphing.ActiveWeightCount} active target(s)");
    }
}
