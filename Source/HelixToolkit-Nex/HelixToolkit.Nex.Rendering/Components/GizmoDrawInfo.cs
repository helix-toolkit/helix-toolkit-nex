using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// ECS component describing one renderable gizmo (Requirement 1).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="GizmoDrawInfo"/> set on an entity describes a single gizmo as data — its
/// handle set, per-handle shape/axis/color/local-transform, the gizmo origin, and render
/// options — mirroring how <see cref="MeshDrawInfo"/>, <c>LineDrawInfo</c>, and
/// <c>PointDrawInfo</c> describe scene geometry. It carries all the data the render node
/// needs to record draws for every handle without reading interaction state held privately
/// by the <c>GizmoManager</c> (Requirement 1.4).
/// </para>
/// <para>
/// The owning entity id is supplied at draw/pick time from the entity carrying the
/// component, so a pick resolves to <em>(owning entity id, <see cref="GizmoHandleId"/>)</em>
/// (Requirement 1.5). New gizmo types that reuse an existing shader shape id require no new
/// component type — they are simply a different <see cref="Handles"/> set
/// (Requirements 1.3, 8.1). Future per-handle data can be added to <see cref="GizmoHandle"/>
/// without invalidating existing gizmos (Requirement 8.4).
/// </para>
/// </remarks>
public struct GizmoDrawInfo
{
    /// <summary>The transform-manipulation mode this gizmo represents.</summary>
    public GizmoMode Mode { get; set; }

    /// <summary>The reference frame the gizmo operates in.</summary>
    public GizmoSpace Space { get; set; }

    /// <summary>The gizmo origin (pivot) in world space; handles are placed relative to it.</summary>
    public Vector3 Origin { get; set; }

    /// <summary>Whether handles draw always-on-top (X-ray) or respect scene depth.</summary>
    public GizmoOcclusionMode OcclusionMode { get; set; }

    /// <summary>Desired on-screen handle size in pixels (clamped when applied).</summary>
    public float DesiredPixelSize { get; set; }

    /// <summary>
    /// The handle set for this gizmo. Each handle carries its stable <see cref="GizmoHandleId"/>
    /// (mode + axis), shape, axis, color, and gizmo-local transform. Screen-size scaling is applied
    /// in the vertex shader, not baked here.
    /// </summary>
    public IReadOnlyList<GizmoHandle> Handles { get; set; }

    /// <summary>True when this component has a non-empty, drawable handle set.</summary>
    public readonly bool Valid => Handles is { Count: > 0 };
}
