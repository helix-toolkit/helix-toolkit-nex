namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Stable per-handle identity of a gizmo handle, keyed by the gizmo mode and the
/// axis (or plane) it constrains.
/// </summary>
/// <remarks>
/// Within one gizmo every handle has a distinct <see cref="GizmoHandleId"/> by
/// construction (one handle per mode + axis). This identity — not a reserved-band
/// entity id — is what a decoded gizmo pick resolves to.
/// </remarks>
/// <param name="Mode">The gizmo mode the handle belongs to.</param>
/// <param name="Axis">The axis or plane the handle constrains.</param>
public readonly record struct GizmoHandleId(GizmoMode Mode, GizmoAxis Axis);

/// <summary>
/// One interactive part of a gizmo (e.g. the X translate arrow).
/// </summary>
/// <remarks>
/// <para>
/// Handle identity is carried by <see cref="Id"/> (mode + axis); the owning entity
/// id is supplied at draw/pick time from the entity carrying the
/// <c>GizmoDrawInfo</c> component. There is no reserved-band entity id on the handle.
/// </para>
/// <para>
/// <see cref="LocalTransform"/> is expressed in gizmo-local space; screen-size
/// scaling is applied in the shader, not baked into the geometry here. Degenerate
/// handles (zero-length line, zero-radius ring) must not be emitted.
/// </para>
/// </remarks>
/// <param name="Id">Stable identity of the handle (mode + axis).</param>
/// <param name="Shape">The visual shape used to draw the handle.</param>
/// <param name="Axis">The axis or plane the handle constrains.</param>
/// <param name="Color">The handle's display color.</param>
/// <param name="LocalTransform">The handle's transform relative to the gizmo origin.</param>
public readonly record struct GizmoHandle(
    GizmoHandleId Id,
    GizmoHandleShape Shape,
    GizmoAxis Axis,
    Color4 Color,
    Matrix4x4 LocalTransform
);
