using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// One gizmo gathered for the current frame.
/// </summary>
/// <param name="WorldId">The id of the world carrying the <see cref="GizmoDrawInfo"/>.</param>
/// <param name="OwningEntityId">The id of the entity carrying the <see cref="GizmoDrawInfo"/>.</param>
/// <param name="Info">The gathered component snapshot for this frame.</param>
public readonly record struct GatheredGizmo(uint WorldId, uint OwningEntityId, GizmoDrawInfo Info);

/// <summary>
/// Gathers all <see cref="GizmoDrawInfo"/> entities each frame (Requirement 2).
/// </summary>
public interface IGizmoDataProvider
{
    /// <summary>The gizmos gathered for the current frame (empty when none are present).</summary>
    IReadOnlyList<GatheredGizmo> Gizmos { get; }
}
