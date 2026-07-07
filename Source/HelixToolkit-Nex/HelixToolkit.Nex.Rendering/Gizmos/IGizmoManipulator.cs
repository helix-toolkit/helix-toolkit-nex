namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Abstraction that owns a gizmo's manipulated target: it reports the target's current transform
/// for gizmo origin placement and applies an incremental drag delta to the bound target, deciding
/// which target properties the delta modifies.
/// </summary>
/// <remarks>
/// The manager only ever calls <see cref="GetTargetTransform"/> (as the gizmo origin source) and
/// <see cref="ApplyDelta"/> (forwarding the raw, uninterpreted drag delta). Which properties the
/// delta modifies is entirely owned by the implementation: the default transform manipulator edits
/// the bound node's TRS transform, while a custom manipulator may drive any node-exposed property.
/// </remarks>
public interface IGizmoManipulator
{
    /// <summary>
    /// Returns the bound target's current world transform. The manager uses this matrix's
    /// translation as the gizmo origin and (for local space) to orient constrained axes.
    /// </summary>
    /// <returns>The bound target's current transform used for gizmo origin placement.</returns>
    Matrix4x4 GetTargetTransform();

    /// <summary>
    /// Applies an incremental drag delta (produced by the drag lifecycle) to the bound target.
    /// The implementation decides how to interpret the delta and which target properties to modify.
    /// </summary>
    /// <param name="dragDelta">The incremental transform delta to apply to the bound target.</param>
    void ApplyDelta(in Matrix4x4 dragDelta);
}
