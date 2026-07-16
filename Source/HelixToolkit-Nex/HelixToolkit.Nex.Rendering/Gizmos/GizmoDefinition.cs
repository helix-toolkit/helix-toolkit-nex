namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// The handle-presentation configuration of a gizmo request: the desired constant on-screen
/// handle size and how handles are occluded against scene depth.
/// </summary>
/// <remarks>
/// Value equality (record struct) participates in <see cref="GizmoDefinition"/> equality, which in
/// turn drives the factory's handle-set cache keying.
/// </remarks>
/// <param name="DesiredPixelSize">The desired constant on-screen handle size, in pixels.</param>
/// <param name="OcclusionMode">Whether handles draw always-on-top or respect scene depth.</param>
public readonly record struct GizmoHandleConfiguration(
    float DesiredPixelSize,
    GizmoOcclusionMode OcclusionMode
);

/// <summary>
/// The full set of inputs that determine a gizmo: its transform mode, reference space, and its
/// handle configuration.
/// </summary>
/// <remarks>
/// <para>
/// Value equality (record struct) defines when two gizmo requests are interchangeable: equal
/// definitions return the same cached handle set from the factory without a rebuild.
/// </para>
/// <para>
/// Handle <em>geometry</em> depends only on the geometry-affecting subset captured by
/// <see cref="ShapeKey"/>; reference space affects placement and per-instance state rather than the
/// generated handles, so the cache is keyed by the shape key.
/// </para>
/// </remarks>
/// <param name="Mode">The transform-manipulation mode the gizmo represents.</param>
/// <param name="Space">The reference frame the gizmo operates in.</param>
/// <param name="Handles">The handle-presentation configuration.</param>
public readonly record struct GizmoDefinition(
    GizmoMode Mode,
    GizmoSpace Space,
    GizmoHandleConfiguration Handles
)
{
    /// <summary>
    /// The geometry-determining subset of this definition used as the handle-set cache key.
    /// </summary>
    internal GizmoShapeKey ShapeKey => new(Mode);

    /// <summary>
    /// Gets a value indicating whether this definition is well-formed: its mode and space are
    /// defined enum values and its desired handle pixel size is a finite number.
    /// </summary>
    /// <remarks>
    /// Centralizes the null/invalid check so the factory rejects invalid requests uniformly.
    /// </remarks>
    public bool IsValid =>
        Enum.IsDefined(Mode)
        && Enum.IsDefined(Space)
        && float.IsFinite(Handles.DesiredPixelSize);
}

/// <summary>
/// The geometry-determining key that identifies a distinct gizmo handle set in the factory cache.
/// </summary>
/// <remarks>
/// Handle geometry is a function of the gizmo mode only; equal <see cref="GizmoDefinition"/> values
/// always produce equal shape keys, so "build once per definition" holds.
/// </remarks>
/// <param name="Mode">The gizmo mode whose handle geometry this key identifies.</param>
internal readonly record struct GizmoShapeKey(GizmoMode Mode);

/// <summary>
/// A lightweight, reusable reference to a gizmo created by the factory. Setting it on an entity
/// associates that entity's cached handle set with the entity.
/// </summary>
/// <remarks>
/// This is an opaque token (a monotonic id) the manager maps to its internal instance state and to
/// the cached handle set. It is distinct from a <see cref="GizmoHandle"/> (one interactive part of a
/// gizmo).
/// </remarks>
/// <param name="Id">The opaque instance id; <c>0</c> denotes the invalid <see cref="None"/> handle.</param>
public readonly record struct GizmoInstanceHandle(int Id)
{
    /// <summary>The sentinel invalid handle, returned when a gizmo could not be created.</summary>
    public static readonly GizmoInstanceHandle None = new(0);

    /// <summary>Gets a value indicating whether this handle references a real gizmo instance.</summary>
    public bool IsValid => Id != 0;
}
