namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Drag-manipulation lifecycle for <see cref="GizmoManager"/>: <see cref="BeginDrag"/>,
/// <see cref="UpdateDrag"/>, and <see cref="EndDrag"/>.
/// </summary>
/// <remarks>
/// This partial extends the core <see cref="GizmoManager"/> with the interactive drag math for all
/// three modes: translate-along-axis, rotate-about-axis, and scale-along-axis. It reuses the
/// drag-state fields declared in the primary file (<c>_isDragging</c>, <c>_dragHandle</c>,
/// <c>_dragAxisWorld</c>, <c>_dragStartPoint</c>) plus <c>_dragPrevVector</c>/<c>_dragPrevAxisPos</c>
/// declared here, together with the per-drag captured frame (<c>_dragOrigin</c>, <c>_dragSpace</c>,
/// <c>_dragTargetTransform</c>, <c>_dragOwningEntityId</c>). Capturing the frame per-drag keeps an
/// active drag anchored to the single originating gizmo it began on, so it manipulates that gizmo and
/// leaves every other tracked gizmo unmodified. The dragged handle's <see cref="GizmoHandleId.Mode"/>
/// selects the manipulation. It satisfies Requirement 7.
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>
    /// The magnitude below which the denominator of the two-line closest-point solve is treated as
    /// degenerate (the pointer ray is parallel to the constrained axis), so no update is produced.
    /// </summary>
    private const float DragParallelEpsilon = 1e-6f;

    /// <summary>Rotate drag: the previous in-plane reference vector (perpendicular to the rotation axis).</summary>
    private Vector3 _dragPrevVector = Vector3.UnitX;

    /// <summary>Scale drag: the previous signed distance of the pointer projection along the axis.</summary>
    private float _dragPrevAxisPos = 1f;

    /// <summary>
    /// Begins a drag on <paramref name="handle"/> using the manager's current per-frame gizmo frame
    /// (origin, space, target transform) captured by the last <see cref="Update"/>, binding the drag
    /// to the managed entity. This is the single-gizmo entry point; the component-driven entry point
    /// is <see cref="BeginDrag(in GizmoPickResolution, in Ray)"/>.
    /// </summary>
    /// <param name="handle">The handle resolved from a pick that the drag will manipulate.</param>
    /// <param name="pointerRay">The world-space pointer ray at the moment the drag begins.</param>
    /// <returns>
    /// <see langword="true"/> if a valid constrained axis and anchor could be determined and the drag
    /// was started; otherwise <see langword="false"/> (and no drag is started).
    /// </returns>
    public bool BeginDrag(GizmoHandleId handle, in Ray pointerRay)
    {
        // Capture the manager's current gizmo frame as the drag frame and bind to the managed entity.
        CaptureDragFrame(_gizmoOrigin, Space, _targetTransform, (uint)_managedEntity.Id);
        return BeginDragCore(handle, pointerRay);
    }

    /// <summary>
    /// Begins a drag from a <see cref="GizmoPickResolution"/> produced by
    /// <see cref="TryResolvePick(bool, uint, GizmoHandleId, out GizmoPickResolution)"/>, driving the
    /// drag from the resolved <em>(owning entity, <see cref="GizmoHandleId"/>)</em> and constraining it
    /// to that single originating gizmo + handle (Requirements 6.6, 7.5, 10.1). The produced
    /// transform-delta behavior is identical to the single-gizmo <see cref="BeginDrag(GizmoHandleId, in Ray)"/>.
    /// </summary>
    /// <param name="resolution">The resolved gizmo pick identifying the owning entity and handle to drag.</param>
    /// <param name="pointerRay">The world-space pointer ray at the moment the drag begins.</param>
    /// <returns>
    /// <see langword="true"/> if the resolution refers to a tracked gizmo that exposes the handle and a
    /// valid constrained axis and anchor could be determined; otherwise <see langword="false"/> (and no
    /// drag is started).
    /// </returns>
    /// <remarks>
    /// The drag is refused for a resolution whose owning entity is not tracked or whose handle the
    /// gizmo does not expose, so an active drag is only ever bound to a genuinely tracked gizmo + handle
    /// (Requirement 7.5). The drag computes only a transform delta and never mutates any gizmo's
    /// <see cref="GizmoDrawInfo"/> component, so every other tracked gizmo is left unmodified
    /// (Requirement 10.1).
    /// </remarks>
    public bool BeginDrag(in GizmoPickResolution resolution, in Ray pointerRay)
    {
        // Only begin a drag for a genuinely tracked gizmo that exposes the resolved handle, so the
        // active drag is constrained to a single originating gizmo + handle (Requirement 7.5).
        if (!_tracked.TryGetValue(resolution.OwningEntityId, out HashSet<GizmoHandleId>? handles)
            || !handles.Contains(resolution.Handle))
        {
            return false;
        }

        // Capture the originating gizmo's frame. The manager describes its gizmo via the captured
        // Update state; bind the drag to the resolved owning entity so it stays isolated to that gizmo.
        CaptureDragFrame(_gizmoOrigin, Space, _targetTransform, resolution.OwningEntityId);
        return BeginDragCore(resolution.Handle, pointerRay);
    }

    /// <summary>
    /// Captures the drag frame (origin, space, target transform, owning entity) an active drag is
    /// anchored to. Captured per-drag so subsequent <see cref="Update"/> calls do not disturb an
    /// active drag's constrained axis and anchor (Requirement 6.6).
    /// </summary>
    private void CaptureDragFrame(Vector3 origin, GizmoSpace space, Matrix4x4 targetTransform, uint owningEntityId)
    {
        _dragOrigin = origin;
        _dragSpace = space;
        _dragTargetTransform = targetTransform;
        _dragOwningEntityId = owningEntityId;
    }

    /// <summary>
    /// Shared drag-start logic operating on the captured drag frame: determines the constrained
    /// world-space axis and captures the appropriate anchor for the handle's mode (the closest point on
    /// the axis line for translate/scale, or the initial in-plane vector for rotate), then marks the
    /// manager as dragging (Requirement 7.1).
    /// </summary>
    private bool BeginDragCore(GizmoHandleId handle, in Ray pointerRay)
    {
        if (!TryGetConstrainedAxis(handle.Axis, out Vector3 axisWorld))
        {
            return false;
        }

        if (handle.Mode == GizmoMode.Rotate)
        {
            // Rotation tracks the pointer's angular position in the plane perpendicular to the axis.
            if (!TryGetInPlaneVector(pointerRay, axisWorld, out Vector3 v0))
            {
                return false;
            }
            _dragPrevVector = v0;
        }
        else
        {
            // Translate/scale track the pointer's projection onto the constrained axis line. If the
            // ray is parallel to the axis we cannot anchor the drag, so refuse to start.
            if (!ClosestPointOnAxis(pointerRay, _dragOrigin, axisWorld, out Vector3 start))
            {
                return false;
            }
            _dragStartPoint = start;
            _dragPrevAxisPos = Vector3.Dot(start - _dragOrigin, axisWorld);
        }

        _dragHandle = handle;
        _dragAxisWorld = axisWorld;
        _isDragging = true;
        return true;
    }

    /// <summary>
    /// Updates an active drag, producing an incremental <paramref name="transformDelta"/> appropriate
    /// for the dragged handle's mode: a pure translation along the axis (Translate), a rotation about
    /// the axis (Rotate), or a scale along the axis (Scale). The delta is expressed so that composing
    /// it as <c>delta * targetWorld</c> manipulates the target about the gizmo origin.
    /// </summary>
    /// <param name="pointerRay">The current world-space pointer ray.</param>
    /// <param name="transformDelta">
    /// On return, the incremental transform to apply to the target, or <see cref="Matrix4x4.Identity"/>
    /// when no update is produced.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a transform delta was produced; <see langword="false"/> if no drag is
    /// active (Requirement 7.6) or the motion could not be determined this frame (Requirement 7.5), in
    /// which case the drag state is preserved.
    /// </returns>
    public bool UpdateDrag(in Ray pointerRay, out Matrix4x4 transformDelta)
    {
        transformDelta = Matrix4x4.Identity;

        // No active drag: identity delta and report no update (Requirement 7.6).
        if (!_isDragging)
        {
            return false;
        }

        return _dragHandle.Mode switch
        {
            GizmoMode.Rotate => UpdateRotateDrag(pointerRay, out transformDelta),
            GizmoMode.Scale => UpdateScaleDrag(pointerRay, out transformDelta),
            _ => UpdateTranslateDrag(pointerRay, out transformDelta),
        };
    }

    /// <summary>
    /// Translate: projects pointer movement onto the constrained axis and outputs a pure translation
    /// parallel to it (Requirements 7.2, 7.3).
    /// </summary>
    private bool UpdateTranslateDrag(in Ray pointerRay, out Matrix4x4 transformDelta)
    {
        transformDelta = Matrix4x4.Identity;

        // When the ray is parallel to the axis this is not determinable: keep the drag state and
        // report no transform (Requirement 7.5).
        if (!ClosestPointOnAxis(pointerRay, _dragOrigin, _dragAxisWorld, out Vector3 current))
        {
            return false;
        }

        float moved = Vector3.Dot(current - _dragStartPoint, _dragAxisWorld);
        transformDelta = Matrix4x4.CreateTranslation(_dragAxisWorld * moved);
        _dragStartPoint = current;
        return true;
    }

    /// <summary>
    /// Scale: forms the ratio of the pointer's current axis projection to the previous one and outputs
    /// an incremental non-uniform scale along the axis about the gizmo origin.
    /// </summary>
    private bool UpdateScaleDrag(in Ray pointerRay, out Matrix4x4 transformDelta)
    {
        transformDelta = Matrix4x4.Identity;

        if (!ClosestPointOnAxis(pointerRay, _dragOrigin, _dragAxisWorld, out Vector3 current))
        {
            return false;
        }

        float axisPos = Vector3.Dot(current - _dragOrigin, _dragAxisWorld);

        // A non-degenerate previous reference is required to form a ratio.
        if (MathF.Abs(_dragPrevAxisPos) <= DragParallelEpsilon)
        {
            _dragPrevAxisPos = axisPos;
            return false;
        }

        float factor = axisPos / _dragPrevAxisPos;
        _dragPrevAxisPos = axisPos;

        // Reject non-finite or non-positive factors (e.g. the pointer crossed the gizmo origin).
        if (!float.IsFinite(factor) || factor <= DragParallelEpsilon)
        {
            return false;
        }

        transformDelta = BuildAxisScaleDelta(_dragAxisWorld, factor);
        return true;
    }

    /// <summary>
    /// Rotate: measures the signed angle the pointer swept in the plane perpendicular to the axis since
    /// the last update and outputs an incremental rotation about the axis (through the gizmo origin).
    /// </summary>
    private bool UpdateRotateDrag(in Ray pointerRay, out Matrix4x4 transformDelta)
    {
        transformDelta = Matrix4x4.Identity;

        if (!TryGetInPlaneVector(pointerRay, _dragAxisWorld, out Vector3 v))
        {
            return false;
        }

        // Signed incremental angle from the previous in-plane vector to the current one, about the axis.
        float angle = MathF.Atan2(
            Vector3.Dot(Vector3.Cross(_dragPrevVector, v), _dragAxisWorld),
            Vector3.Dot(_dragPrevVector, v));
        _dragPrevVector = v;

        if (!float.IsFinite(angle) || MathF.Abs(angle) <= DragParallelEpsilon)
        {
            return false;
        }

        transformDelta = Matrix4x4.CreateFromAxisAngle(_dragAxisWorld, angle);
        return true;
    }

    /// <summary>
    /// Intersects the pointer ray with the plane through the gizmo origin whose normal is
    /// <paramref name="axisWorld"/>, returning the in-plane vector from the origin to the hit point
    /// (the axis component removed). Used by rotation drags to measure angular motion around the axis.
    /// Returns <see langword="false"/> when the ray is parallel to the plane or the hit coincides with
    /// the origin, so the angle is not determinable.
    /// </summary>
    private bool TryGetInPlaneVector(in Ray ray, Vector3 axisWorld, out Vector3 inPlane)
    {
        inPlane = Vector3.Zero;

        float denom = Vector3.Dot(ray.Direction, axisWorld);
        if (MathF.Abs(denom) <= DragParallelEpsilon)
        {
            return false;
        }

        float t = Vector3.Dot(_dragOrigin - ray.Position, axisWorld) / denom;
        Vector3 hit = ray.Position + (ray.Direction * t);

        Vector3 v = hit - _dragOrigin;
        v -= Vector3.Dot(v, axisWorld) * axisWorld; // project onto the rotation plane

        if (v.Length() <= DragParallelEpsilon)
        {
            return false;
        }

        inPlane = v;
        return true;
    }

    /// <summary>
    /// Builds a pure linear transform that scales space by <paramref name="factor"/> along
    /// <paramref name="axisWorld"/> only (identity in the perpendicular directions). Composed as
    /// <c>delta * targetWorld</c> it scales the target about the gizmo origin along the axis.
    /// </summary>
    private static Matrix4x4 BuildAxisScaleDelta(Vector3 axisWorld, float factor)
    {
        float k = factor - 1f;
        float ax = axisWorld.X;
        float ay = axisWorld.Y;
        float az = axisWorld.Z;

        // Identity + (factor - 1) * outer(axis, axis). Symmetric, so row/column convention is moot.
        return new Matrix4x4(
            1f + (k * ax * ax), k * ax * ay, k * ax * az, 0f,
            k * ay * ax, 1f + (k * ay * ay), k * ay * az, 0f,
            k * az * ax, k * az * ay, 1f + (k * az * az), 0f,
            0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// Ends the active drag, setting the dragging state to inactive (Requirement 7.4).
    /// </summary>
    public void EndDrag() => _isDragging = false;

    /// <summary>
    /// Resolves the world-space unit axis a handle constrains a translate drag to. Axis handles
    /// (<see cref="GizmoAxis.X"/>, <see cref="GizmoAxis.Y"/>, <see cref="GizmoAxis.Z"/>) map to the
    /// corresponding unit axis, oriented by the captured drag target's rotation when the captured drag
    /// space is <see cref="GizmoSpace.Local"/>. Plane, screen, and uniform handles have no single
    /// translation axis and are rejected.
    /// </summary>
    /// <param name="axis">The axis the handle constrains.</param>
    /// <param name="axisWorld">On success, the normalized world-space axis direction.</param>
    /// <returns><see langword="true"/> if a valid single axis was determined; otherwise <see langword="false"/>.</returns>
    private bool TryGetConstrainedAxis(GizmoAxis axis, out Vector3 axisWorld)
    {
        axisWorld = Vector3.Zero;

        Vector3 local = axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        if (local == Vector3.Zero)
        {
            return false;
        }

        // World space uses the canonical axis directly; local space orients the axis by the target's
        // rotation (TransformNormal carries any target rotation/scale, which normalization collapses to
        // a direction). The captured drag frame keeps an active drag anchored to its originating gizmo.
        Vector3 dir = _dragSpace == GizmoSpace.Local
            ? Vector3.TransformNormal(local, _dragTargetTransform)
            : local;

        float length = dir.Length();
        if (length <= DragParallelEpsilon)
        {
            return false;
        }

        axisWorld = dir / length;
        return true;
    }

    /// <summary>
    /// Computes the point on the axis line <c>axisOrigin + t * axisDir</c> that is closest to
    /// <paramref name="ray"/> using the standard closest-point-between-two-lines solve.
    /// </summary>
    /// <param name="ray">The pointer ray (its direction is assumed normalized).</param>
    /// <param name="axisOrigin">A point the axis line passes through (the gizmo origin).</param>
    /// <param name="axisDir">The normalized axis direction.</param>
    /// <param name="point">On success, the closest point on the axis line to the ray.</param>
    /// <returns>
    /// <see langword="false"/> when the ray is (near-)parallel to the axis, in which case the closest
    /// point is not uniquely determinable; otherwise <see langword="true"/>.
    /// </returns>
    private static bool ClosestPointOnAxis(in Ray ray, Vector3 axisOrigin, Vector3 axisDir, out Vector3 point)
    {
        point = axisOrigin;

        Vector3 d1 = axisDir;              // axis direction (unit)
        Vector3 d2 = ray.Direction;        // ray direction (unit)
        Vector3 w0 = axisOrigin - ray.Position;

        float b = Vector3.Dot(d1, d2);
        float denom = 1f - (b * b);        // a*c - b*b with a = c = 1 for unit directions

        // Near-parallel lines: the closest point along the axis is not uniquely determinable.
        if (MathF.Abs(denom) <= DragParallelEpsilon)
        {
            return false;
        }

        float d = Vector3.Dot(d1, w0);
        float e = Vector3.Dot(d2, w0);
        float t = ((b * e) - d) / denom;

        point = axisOrigin + (d1 * t);
        return true;
    }
}
