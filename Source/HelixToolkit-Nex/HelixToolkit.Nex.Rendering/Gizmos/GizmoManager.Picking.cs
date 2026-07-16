namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// The result of resolving a decoded gizmo pick to a tracked gizmo: the owning entity that carries
/// the gizmo and the stable identity of the picked handle.
/// </summary>
/// <param name="OwningEntityId">The id of the entity carrying the resolved gizmo.</param>
/// <param name="Handle">The stable <see cref="GizmoHandleId"/> of the resolved handle.</param>
public readonly record struct GizmoPickResolution(uint OwningEntityId, GizmoHandleId Handle);

/// <summary>
/// Picking-resolution surface of <see cref="GizmoManager"/>: maps an entity id read back from
/// <c>TextureEntityId</c> to the stable identity of an active handle.
/// </summary>
/// <remarks>
/// <para>
/// These methods are pure lookups against the reverse <c>entityId -&gt; GizmoHandleId</c> map that
/// <see cref="GizmoManager.RebuildHandles"/> populates each frame. They never mutate manager state,
/// so resolving the same id repeatedly always yields the same result (Requirement 6.5), and they
/// never throw for any input (Requirements 6.2, 6.3, 6.4, 10.1).
/// </para>
/// <para>
/// <b>Picking / texture-read assumption.</b> The engine's async readback path
/// (<c>PickingContext.ReadResult</c>) returns a packed <see langword="ulong"/> that the caller
/// decodes into an entity id before calling <see cref="TryResolveHandle(uint, out GizmoHandleId)"/>;
/// that decode + GPU readback wiring lives in the picking/integration layer (outside this task's
/// scope). To satisfy the out-of-bounds and missing/empty-target requirements without coupling this
/// service to a concrete render context or texture-download API, the pixel-oriented overload
/// <see cref="TryResolveHandleAt(Vector2, Size, uint?, out GizmoHandleId)"/> accepts the already
/// read (or unavailable) entity-id value plus the target bounds and applies the sentinel/bounds
/// guards defensively.
/// </para>
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>
    /// Resolves the entity id read from the entity-id target to the identity of the active handle
    /// it maps to, if any.
    /// </summary>
    /// <param name="entityId">
    /// The entity id read from <c>TextureEntityId</c> (already decoded from the packed picking readback).
    /// </param>
    /// <param name="handle">
    /// When this method returns <see langword="true"/>, the identity of the resolved active handle;
    /// otherwise <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if and only if <paramref name="entityId"/> maps to a currently active
    /// handle; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This is a side-effect-free lookup:
    /// <list type="bullet">
    /// <item>An id that maps to no active handle returns <see langword="false"/> without throwing
    /// (Requirement 6.3).</item>
    /// <item>Manager state is never modified, so repeated calls with the same id yield the same
    /// result (Requirement 6.5).</item>
    /// </list>
    /// </remarks>
    public bool TryResolveHandle(uint entityId, out GizmoHandleId handle)
    {
        handle = default;

        // Ids that map to no active handle fall through to false here (Requirement 6.3).
        // Dictionary.TryGetValue is a pure read: no state is mutated (Requirement 6.5).
        return _entityIdToHandle.TryGetValue(entityId, out handle);
    }

    /// <summary>
    /// Resolves a handle from a pointer pixel coordinate against the entity-id target, applying the
    /// out-of-bounds and missing/empty-target guards before delegating to
    /// <see cref="TryResolveHandle(uint, out GizmoHandleId)"/>.
    /// </summary>
    /// <param name="pointerPixel">
    /// The pointer position in entity-id-target pixel space. Fractional coordinates are floored to
    /// the covering pixel.
    /// </param>
    /// <param name="targetSize">
    /// The pixel dimensions of the entity-id target. An empty size (zero width or height) represents
    /// a missing, unbound, or empty target.
    /// </param>
    /// <param name="entityIdAtPixel">
    /// The entity id read at <paramref name="pointerPixel"/>, or <see langword="null"/> when no value
    /// could be read (missing/unbound/empty target, or an unreadable pixel). A <see langword="null"/>
    /// value is treated as the "no hit" sentinel.
    /// </param>
    /// <param name="handle">
    /// When this method returns <see langword="true"/>, the identity of the resolved active handle;
    /// otherwise <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if and only if the coordinate is inside the target bounds and the read
    /// entity id maps to an active gizmo handle; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The method is defensive and exception-free, and never mutates manager state:
    /// <list type="bullet">
    /// <item>A missing, unbound, or empty entity-id target (empty <paramref name="targetSize"/>) is
    /// treated as the "no hit" sentinel and returns <see langword="false"/> (Requirements 10.1, 10.2).</item>
    /// <item>A pointer coordinate outside the target bounds returns <see langword="false"/>
    /// (Requirement 6.4).</item>
    /// <item>A <see langword="null"/> <paramref name="entityIdAtPixel"/> is treated as the "no hit"
    /// sentinel (Requirement 10.1).</item>
    /// </list>
    /// </remarks>
    public bool TryResolveHandleAt(
        Vector2 pointerPixel,
        Size targetSize,
        uint? entityIdAtPixel,
        out GizmoHandleId handle
    )
    {
        handle = default;

        // Requirements 10.1 / 10.2: a missing, unbound, or empty entity-id target (zero readable
        // pixels) is treated as the "no hit" sentinel — report no resolution, do not throw.
        if (targetSize.IsEmpty)
        {
            return false;
        }

        // Requirement 6.4: a pointer coordinate outside the target bounds resolves to no handle.
        // Floor so fractional pointer positions map to the pixel they fall inside; negative
        // coordinates floor below zero and are rejected here.
        int px = (int)MathF.Floor(pointerPixel.X);
        int py = (int)MathF.Floor(pointerPixel.Y);
        if (px < 0 || py < 0 || px >= targetSize.Width || py >= targetSize.Height)
        {
            return false;
        }

        // Requirement 10.1: an unreadable pixel (null) is treated as "no hit" — report no
        // resolution without throwing.
        if (entityIdAtPixel is not uint entityId)
        {
            return false;
        }

        return TryResolveHandle(entityId, out handle);
    }

    /// <summary>
    /// Resolves a decoded entity-id pick to the gizmo and handle it identifies, if and only if the
    /// decode is a gizmo pick, its owning entity is currently tracked, and the decoded handle id is
    /// one this gizmo exposes.
    /// </summary>
    /// <param name="isGizmo">
    /// Whether the decode classified the pixel as a gizmo pick (i.e. the Engine's
    /// <c>EntityIdDecodeResult.Kind == Gizmo</c>). Scene and no-hit decodes pass <see langword="false"/>.
    /// </param>
    /// <param name="owningEntityId">The decoded owning entity id carried in the R channel (gizmo picks only).</param>
    /// <param name="handle">The decoded handle identity carried in the G channel (gizmo picks only).</param>
    /// <param name="resolution">
    /// When this method returns <see langword="true"/>, the resolved
    /// <see cref="GizmoPickResolution"/> (owning entity + handle); otherwise <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if and only if <paramref name="isGizmo"/> is set, the owning entity is
    /// tracked, and the gizmo exposes <paramref name="handle"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This mirrors the design's <c>TryResolvePick(in EntityIdDecodeResult, out GizmoPickResolution)</c>
    /// with a signature that uses only <c>HelixToolkit.Nex.Rendering</c>-visible types.
    /// <c>EntityIdDecodeResult</c> and <c>EntityIdPickKind</c> live in
    /// <c>HelixToolkit.Nex.Engine</c>, which references <c>HelixToolkit.Nex.Rendering</c> (not the
    /// reverse — see the project references and the <c>InternalsVisibleTo</c> in
    /// <c>HelixToolkit.Nex.Rendering.csproj</c>). Referencing those Engine types here would create a
    /// dependency cycle, so the caller in the Engine/picking layer performs the decode and passes the
    /// three decoded fields (<c>Kind == Gizmo</c> as <paramref name="isGizmo"/>, the owning entity id,
    /// and the handle id). The resolution semantics are identical to the design.
    /// </para>
    /// <para>
    /// The lookup is pure and total:
    /// <list type="bullet">
    /// <item>Scene / no-hit decodes (<paramref name="isGizmo"/> is <see langword="false"/>) resolve to
    /// nothing without throwing (Requirements 6.3, 6.4).</item>
    /// <item>An untracked owning entity, or an unexposed/malformed handle id, resolves to nothing
    /// without throwing (Requirements 6.3, 7.3).</item>
    /// <item>Tracking state is never mutated, so resolving the same input repeatedly yields the same
    /// result (Requirements 6.5, 10.5).</item>
    /// </list>
    /// </para>
    /// </remarks>
    public bool TryResolvePick(
        bool isGizmo,
        uint owningEntityId,
        GizmoHandleId handle,
        out GizmoPickResolution resolution
    )
    {
        resolution = default;

        // Not a gizmo pick (scene / no-hit) -> no resolution (Requirements 6.3, 6.4).
        if (!isGizmo)
        {
            return false;
        }

        // Owning entity is not tracked -> no resolution (Requirement 7.3). TryGetValue is a pure read.
        if (!_tracked.TryGetValue(owningEntityId, out HashSet<GizmoHandleId>? handles))
        {
            return false;
        }

        // Handle id is not one this gizmo exposes (unexposed / malformed) -> no resolution
        // (Requirements 6.2, 7.2). HashSet.Contains is a pure read.
        if (!handles.Contains(handle))
        {
            return false;
        }

        resolution = new GizmoPickResolution(owningEntityId, handle);
        return true;
    }
}
