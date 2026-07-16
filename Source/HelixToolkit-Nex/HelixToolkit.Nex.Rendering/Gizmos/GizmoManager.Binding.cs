namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Runtime target-binding surface of the <see cref="GizmoManager"/>: associates a tracked gizmo
/// instance with an <see cref="IGizmoManipulator"/> that owns the manipulated target. The binding is
/// the single source of truth for the gizmo origin (read each frame from
/// <see cref="IGizmoManipulator.GetTargetTransform"/>) and the destination for produced drag deltas
/// (applied via <see cref="IGizmoManipulator.ApplyDelta"/>).
/// </summary>
/// <remarks>
/// Bindings live alongside the factory's <c>_instances</c> map and are keyed by the same
/// <see cref="GizmoInstanceHandle"/>, so a binding only ever references a created, still-tracked
/// gizmo. Binding a resolvable handle with a non-null manipulator records (or replaces) the active
/// manipulator; a null manipulator or an unresolvable handle leaves every existing binding unchanged.
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>Per-instance manipulator binding, keyed by the instance handle.</summary>
    private readonly Dictionary<GizmoInstanceHandle, IGizmoManipulator> _bindings = [];

    /// <summary>
    /// Binds (or rebinds) the manipulator for a tracked gizmo instance (Requirements 2.1, 2.2, 2.5,
    /// 7.1). Any <see cref="IGizmoManipulator"/> implementation is accepted.
    /// </summary>
    /// <param name="handle">The handle of the gizmo instance to bind the manipulator to.</param>
    /// <param name="manipulator">The manipulator that owns the manipulated target.</param>
    /// <returns>
    /// <see langword="true"/> when the manipulator was recorded as the active binding for the
    /// instance; <see langword="false"/> when <paramref name="manipulator"/> is <see langword="null"/>
    /// (Requirement 2.4) or <paramref name="handle"/> is unresolvable
    /// (<see cref="GizmoInstanceHandle.None"/>, never created, or already removed) (Requirement 2.3),
    /// in which case all existing bindings are left unchanged.
    /// </returns>
    public bool BindTarget(in GizmoInstanceHandle handle, IGizmoManipulator manipulator)
    {
        ThrowIfDisposed();

        // Req 2.4: a null manipulator leaves the existing binding unchanged and reports failure.
        if (manipulator is null)
        {
            return false;
        }

        // Req 2.3: an unresolvable handle leaves all bindings unchanged and reports failure.
        if (!handle.IsValid || !_instances.ContainsKey(handle))
        {
            return false;
        }

        // Req 2.2, 2.5: record the manipulator as the active binding, replacing any previous one.
        _bindings[handle] = manipulator;
        return true;
    }

    /// <summary>
    /// Attempts to resolve the manipulator currently bound to <paramref name="handle"/>.
    /// </summary>
    /// <param name="handle">The handle of the gizmo instance to look up.</param>
    /// <param name="manipulator">
    /// On success, the manipulator bound to the instance; otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when a binding exists for the handle; otherwise <see langword="false"/>.</returns>
    private bool TryGetBinding(in GizmoInstanceHandle handle, out IGizmoManipulator? manipulator)
        => _bindings.TryGetValue(handle, out manipulator);
}
