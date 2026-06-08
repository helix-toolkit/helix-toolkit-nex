using System.Numerics;
using HelixToolkit.Nex.Scene;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace HelixToolkit.Nex.Sample.GltfImporter;

/// <summary>
/// Renders the scene graph as a collapsible ImGui tree view.
/// Handles click events to drive selection via SelectionManager.
/// </summary>
internal class SceneTreePanel
{
    private readonly SelectionManager _selectionManager;

    public SceneTreePanel(SelectionManager selectionManager)
    {
        _selectionManager =
            selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
    }

    /// <summary>
    /// Draws the scene tree panel. If rootNode is null, displays a
    /// "No scene available" disabled text indicator.
    /// </summary>
    public void Draw(Node? rootNode, Vector2 position, Vector2 size)
    {
        Gui.SetNextWindowPos(position);
        Gui.SetNextWindowSize(size);

        if (Gui.Begin("Scene Tree", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            if (rootNode is null)
            {
                Gui.BeginDisabled();
                Gui.TextUnformatted("No scene available");
                Gui.EndDisabled();
            }
            else
            {
                DrawNodeTree(rootNode);
            }
        }

        Gui.End();
    }

    /// <summary>
    /// Recursively draws a node and its children as ImGui tree nodes.
    /// - Uses node.Name or "Entity {id}" as the label
    /// - Leaf nodes render with bullet indicator (no expand arrow)
    /// - Selected node has ImGuiTreeNodeFlags.Selected
    /// - Click triggers SelectionManager.ToggleSelect
    /// </summary>
    private void DrawNodeTree(Node node)
    {
        var entity = node.Entity;

        // Determine the display label
        var name = node.Name;
        var label = string.IsNullOrEmpty(name) ? $"Entity {entity.Id}" : name;

        // Build flags
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;

        // Leaf nodes: no expand arrow, show bullet
        if (!node.HasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet;
        }

        // Selected node styling
        if (_selectionManager.SelectedEntity == entity)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        // Render the tree node with a unique ID based on entity ID
        bool isOpen = Gui.TreeNodeEx($"{label}##{entity.Id}", flags);

        // Handle click on this tree node
        if (Gui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _selectionManager.ToggleSelect(entity, node);
        }

        // Recursively draw children if the node is expanded
        if (isOpen)
        {
            var children = node.Children;
            if (children is not null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    DrawNodeTree(children[i]);
                }
            }

            Gui.TreePop();
        }
    }
}
