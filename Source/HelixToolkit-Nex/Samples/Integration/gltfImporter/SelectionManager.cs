using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Scene;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;

namespace HelixToolkit.Nex.Sample.GltfImporter;

/// <summary>
/// Manages the currently selected entity across all selection sources
/// (viewport picking, scene tree clicks). Applies and removes visual
/// highlight overlays to maintain consistent selection state.
/// </summary>
internal class SelectionManager
{
    private Entity _selectedEntity = Entity.Null;
    private Node? _selectedNode;

    /// <summary>Gets the currently selected entity, or Entity.Null if nothing is selected.</summary>
    public Entity SelectedEntity => _selectedEntity;

    /// <summary>Gets the currently selected node, or null if nothing is selected.</summary>
    public Node? SelectedNode => _selectedNode;

    /// <summary>Gets whether an entity is currently selected.</summary>
    public bool HasSelection => _selectedEntity.Valid;

    /// <summary>
    /// Selects the given entity. Removes highlight from the previously selected entity
    /// (if different) and applies BorderHighlightOverlay to the new one.
    /// If the entity is already selected, this is a no-op (viewport behavior).
    /// </summary>
    public void Select(Entity entity, Node? node)
    {
        // No-op if same entity already selected (Req 4.5)
        if (_selectedEntity == entity)
        {
            return;
        }

        // Remove highlight from previous entity if one was selected (Req 3.8, 4.3)
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Remove<BorderHighlightOverlay>();
        }

        // Apply highlight to new entity
        _selectedEntity = entity;
        _selectedNode = node;

        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightOverlay.Default);
        }
    }

    /// <summary>
    /// Deselects the current entity, removing its highlight overlay.
    /// </summary>
    public void Deselect()
    {
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Remove<BorderHighlightOverlay>();
        }

        _selectedEntity = Entity.Null;
        _selectedNode = null;
    }

    /// <summary>
    /// Toggles selection: if the entity is already selected, deselects it;
    /// otherwise selects it. Used by the scene tree click behavior.
    /// </summary>
    public void ToggleSelect(Entity entity, Node? node)
    {
        // If entity is already selected, deselect (Req 3.6)
        if (_selectedEntity == entity)
        {
            Deselect();
        }
        else
        {
            // Otherwise select the new entity
            Select(entity, node);
        }
    }
}
