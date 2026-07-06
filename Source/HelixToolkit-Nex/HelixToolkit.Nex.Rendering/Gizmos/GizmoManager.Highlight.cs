namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Hover / active-handle highlighting for <see cref="GizmoManager"/>.
/// </summary>
/// <remarks>
/// The editor resolves the handle under the pointer from a decoded gizmo pick (see
/// <see cref="TryResolvePick(bool, uint, GizmoHandleId, out GizmoPickResolution)"/>) and assigns it to
/// <see cref="HoveredHandle"/>. Highlighting is baked into the published <c>GizmoDrawInfo</c>
/// component: when the hovered or dragged handle changes the manager republishes the component with
/// <see cref="ResolveDrawColor(in GizmoHandle)"/> applied per handle, so the pure-consumer render node
/// draws the highlight straight from the component color. This is presentation-only: it never changes
/// the emitted geometry, entity ids, or picking behavior.
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>The handle currently under the pointer, or <see langword="null"/> when none is hovered.</summary>
    private GizmoHandleId? _hoveredHandle;

    /// <summary>
    /// Gets or sets the handle currently under the pointer, or <see langword="null"/> when the pointer
    /// is not over any gizmo handle. Highlighted with <see cref="HighlightColor"/> when drawn. Setting
    /// a different value republishes the managed <c>GizmoDrawInfo</c> component so the highlight takes
    /// effect immediately, without waiting for the next <see cref="Update"/>.
    /// </summary>
    public GizmoHandleId? HoveredHandle
    {
        get => _hoveredHandle;
        set
        {
            if (Nullable.Equals(_hoveredHandle, value))
            {
                return;
            }

            _hoveredHandle = value;

            // Re-publish so the render node picks up the new highlight color this frame. Cheap: it
            // only rebuilds the component snapshot from the already-generated handle set.
            SyncManagedComponent();
        }
    }

    /// <summary>
    /// Gets or sets the color used to highlight the hovered handle and the handle being dragged.
    /// Defaults to yellow.
    /// </summary>
    public Color4 HighlightColor { get; set; } = new(1f, 1f, 0f, 1f);

    /// <summary>
    /// Routes a decoded gizmo pick to the per-instance highlight overlay: when the pick resolves to
    /// exactly one tracked gizmo, that gizmo's resolved handle is highlighted and every other tracked
    /// instance's highlight is cleared (isolation, Requirement 7.2); when the pick resolves to no
    /// tracked gizmo, any active highlight is cleared from all instances (Requirement 7.8).
    /// </summary>
    /// <param name="isGizmo">Whether the decode classified the pixel as a gizmo pick.</param>
    /// <param name="owningEntityId">The decoded owning entity id (gizmo picks only).</param>
    /// <param name="handle">The decoded handle identity (gizmo picks only).</param>
    /// <remarks>
    /// This never rebuilds or reallocates a handle set: it only mutates each affected instance's
    /// <see cref="GizmoHandleId"/> highlight overlay in place and republishes the component
    /// (Requirement 3.3). It is a pure routing helper over the existing pick resolution, so it never
    /// throws for any input.
    /// </remarks>
    public void ResolveHighlight(bool isGizmo, uint owningEntityId, GizmoHandleId handle)
    {
        // A pick that resolves to no tracked gizmo clears every active highlight (Requirement 7.8).
        if (!TryResolvePick(isGizmo, owningEntityId, handle, out GizmoPickResolution resolution))
        {
            ClearAllHighlights();
            return;
        }

        // A resolved pick highlights only the resolved gizmo's handle and clears all others, so the
        // highlight is isolated to a single gizmo (Requirement 7.2).
        foreach (var instance in _instances.Values.AsValueEnumerable().Where(x => x is not null))
        {
            GizmoHandleId? desired =
                instance.HasEntity && (uint)instance.Entity.Id == resolution.OwningEntityId
                    ? resolution.Handle
                    : null;

            if (!Nullable.Equals(instance.Highlighted, desired))
            {
                instance.Highlighted = desired;
                PublishDrawInfo(instance);
            }
        }
    }

    /// <summary>
    /// Clears the highlight overlay from every tracked instance whose highlight is currently set,
    /// republishing only the instances that change. Used when a pick resolves to no tracked gizmo
    /// (Requirement 7.8).
    /// </summary>
    private void ClearAllHighlights()
    {
        foreach (var instance in _instances.Values.AsValueEnumerable().Where(x => x is not null))
        {
            instance.Highlighted = null;
            PublishDrawInfo(instance);
        }
    }

    /// <summary>
    /// Returns the color a handle should be published with this frame: <see cref="HighlightColor"/>
    /// when the handle is being dragged or is currently hovered, otherwise the handle's own color.
    /// Applied by <see cref="Update"/> / the <see cref="HoveredHandle"/> setter when building the
    /// <c>GizmoDrawInfo</c> snapshot, so the pure-consumer render node draws the resolved color
    /// directly from the component.
    /// </summary>
    /// <param name="handle">The handle about to be published.</param>
    /// <returns>The resolved draw color.</returns>
    public Color4 ResolveDrawColor(in GizmoHandle handle)
    {
        if (_isDragging && handle.Id.Equals(_dragHandle))
        {
            return HighlightColor;
        }

        if (_hoveredHandle is GizmoHandleId hovered && handle.Id.Equals(hovered))
        {
            return HighlightColor;
        }

        return handle.Color;
    }
}
