using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Engine-side adapter that lets a <see cref="GizmoManager"/> resolve a decoded
/// <see cref="EntityIdDecodeResult"/> directly, matching the design's
/// <c>TryResolvePick(in EntityIdDecodeResult, out GizmoPickResolution)</c> surface.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GizmoManager"/> and <see cref="GizmoPickResolution"/> live in
/// <c>HelixToolkit.Nex.Rendering</c>, while <see cref="EntityIdDecodeResult"/> and
/// <see cref="EntityIdPickKind"/> live in <c>HelixToolkit.Nex.Engine</c>, which references
/// <c>HelixToolkit.Nex.Rendering</c> (not the reverse). Placing this adapter in the Engine layer
/// keeps the resolution logic — and the manager's tracking state — in the manager, while giving
/// Engine/picking-layer callers the exact "consume a decoded <see cref="EntityIdDecodeResult"/>"
/// entry point from the design (Requirements 6.2, 10.5).
/// </para>
/// <para>
/// The adapter is a pure pass-through: it never mutates the manager's tracking state and never
/// throws, so resolving the same decode repeatedly always yields the same result
/// (Requirements 6.5, 10.5).
/// </para>
/// </remarks>
public static class GizmoPickResolutionExtensions
{
    /// <summary>
    /// Resolves a decoded entity-id pick against the gizmos <paramref name="manager"/> tracks,
    /// succeeding only when the decode is a gizmo pick (<see cref="EntityIdPickKind.Gizmo"/>), its
    /// owning entity is tracked, and the decoded handle id is one that gizmo exposes.
    /// </summary>
    /// <param name="manager">The gizmo manager whose tracked gizmos the pick is resolved against.</param>
    /// <param name="decoded">
    /// The unified decode result (from <see cref="Utils.UnpackEntityId(uint, uint)"/>). Scene and
    /// no-hit decodes resolve to nothing.
    /// </param>
    /// <param name="resolution">
    /// When this method returns <see langword="true"/>, the resolved
    /// <see cref="GizmoPickResolution"/> (owning entity + handle); otherwise <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if and only if <paramref name="decoded"/> is a gizmo pick whose owning
    /// entity is tracked and whose handle id that gizmo exposes; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Pure and total:
    /// <list type="bullet">
    /// <item>Scene / no-hit decodes resolve to nothing without throwing (Requirements 6.4, 7.3).</item>
    /// <item>Untracked owning entities and unexposed / malformed handle ids resolve to nothing
    /// without throwing (Requirements 6.3, 7.3).</item>
    /// <item>Tracking state is never mutated, so repeated resolves are idempotent (Requirements 6.5, 10.5).</item>
    /// </list>
    /// </remarks>
    public static bool TryResolvePick(
        this GizmoManager manager,
        in EntityIdDecodeResult decoded,
        out GizmoPickResolution resolution
    )
    {
        ArgumentNullException.ThrowIfNull(manager);

        return manager.TryResolvePick(
            decoded.Kind == EntityIdPickKind.Gizmo,
            decoded.OwningEntityId,
            decoded.Handle,
            out resolution
        );
    }
}
