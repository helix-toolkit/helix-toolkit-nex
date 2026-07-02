namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// The transform-manipulation mode a gizmo represents.
/// </summary>
public enum GizmoMode
{
    /// <summary>Directional translate handles.</summary>
    Translate,

    /// <summary>Rotation ring handles.</summary>
    Rotate,

    /// <summary>Scale handles.</summary>
    Scale,
}

/// <summary>
/// The reference frame a gizmo operates in.
/// </summary>
public enum GizmoSpace
{
    /// <summary>Handles aligned to world axes.</summary>
    World,

    /// <summary>Handles aligned to the target's local axes.</summary>
    Local,
}

/// <summary>
/// The axis or plane a handle is constrained to.
/// </summary>
public enum GizmoAxis
{
    /// <summary>The gizmo-local X axis.</summary>
    X,

    /// <summary>The gizmo-local Y axis.</summary>
    Y,

    /// <summary>The gizmo-local Z axis.</summary>
    Z,

    /// <summary>The gizmo-local XY plane.</summary>
    XY,

    /// <summary>The gizmo-local YZ plane.</summary>
    YZ,

    /// <summary>The gizmo-local XZ plane.</summary>
    XZ,

    /// <summary>The screen-facing plane.</summary>
    Screen,

    /// <summary>Uniform (all axes) manipulation.</summary>
    Uniform,
}

/// <summary>
/// The visual shape of a handle, which determines how it is drawn.
/// </summary>
public enum GizmoHandleShape
{
    /// <summary>A directional arrow (solid handle).</summary>
    Arrow,

    /// <summary>A rotation ring (line-based handle).</summary>
    Ring,

    /// <summary>A solid box (solid handle).</summary>
    Box,

    /// <summary>A planar quad (solid handle).</summary>
    Plane,

    /// <summary>A straight line segment (line-based handle).</summary>
    Line,
}

/// <summary>
/// Determines whether handles draw always-on-top or respect scene depth.
/// </summary>
public enum GizmoOcclusionMode
{
    /// <summary>Handles draw over scene geometry regardless of depth (X-ray).</summary>
    AlwaysOnTop,

    /// <summary>Handles are depth-tested against the scene depth buffer.</summary>
    DepthTested,
}
