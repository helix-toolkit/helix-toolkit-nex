using System.Numerics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;
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

    public PropertiesPanel(
        SelectionManager selectionManager,
        WorldDataProvider worldDataProvider,
        RenderContext context
    )
    {
        _selectionManager =
            selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
        _worldDataProvider =
            worldDataProvider ?? throw new ArgumentNullException(nameof(worldDataProvider));
        _renderContext = context ?? throw new ArgumentNullException(nameof(context));
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
            if (Gui.BeginChild("SettingsContent", new Vector2(0, 30)))
            {
                var wireframe = _renderContext.RenderParams.EnableGlobalWireframe;
                Gui.Checkbox("Wireframe Mode", ref wireframe);
                _renderContext.RenderParams.EnableGlobalWireframe = wireframe;
                Gui.EndChild();
            }
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
                Gui.EndChild();
            }

            Gui.End();
        }
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
        if (entity.TryGet<MeshComponent>(out var mesh))
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
        }
    }
}
