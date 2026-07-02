using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Alternate encodings selected when a decoded pixel's world id is zero.
/// </summary>
/// <remarks>
/// World id greater than zero always selects the unchanged scene decode
/// (<see cref="Utils.UnpackMeshInfo(uint, uint, out uint, out uint, out uint, out uint)"/>),
/// so adding values here never affects world-id-based discrimination of scene picks.
/// <see cref="None"/> is deliberately <c>0</c> so the cleared-to-<c>(0,0)</c> picking
/// target decodes as "no hit" with no extra plumbing.
/// </remarks>
public enum EntityIdEncoding : uint
{
    /// <summary>Reserved: unrecognized / cleared pixel -&gt; No hit.</summary>
    None = 0,

    /// <summary>Gizmo pick encoding.</summary>
    Gizmo = 1,

    // Future encodings append here without changing world-id-based discrimination.
}

/// <summary>
/// Which category a decoded entity-id pixel falls into.
/// </summary>
public enum EntityIdPickKind
{
    /// <summary>A normal scene (ECS) entity pick (world id &gt; 0).</summary>
    Scene,

    /// <summary>A gizmo handle pick.</summary>
    Gizmo,

    /// <summary>No entity was covered, or the pixel is not a recognized encoding.</summary>
    NoHit,
}

/// <summary>
/// The fully-decoded result of a unified entity-id decode.
/// </summary>
/// <remarks>
/// The scene fields (<see cref="WorldId"/>, <see cref="EntityId"/>,
/// <see cref="InstanceId"/>, <see cref="PrimitiveId"/>) are populated only when
/// <see cref="Kind"/> is <see cref="EntityIdPickKind.Scene"/>; the gizmo fields
/// (<see cref="OwningEntityId"/>, <see cref="Handle"/>) are populated only when
/// <see cref="Kind"/> is <see cref="EntityIdPickKind.Gizmo"/>.
/// </remarks>
/// <param name="Kind">The category this pixel decoded to.</param>
/// <param name="WorldId">Scene world id (scene picks only).</param>
/// <param name="EntityId">Scene entity id (scene picks only).</param>
/// <param name="InstanceId">Scene instance id (scene picks only).</param>
/// <param name="PrimitiveId">Scene primitive id (scene picks only).</param>
/// <param name="OwningEntityId">The id of the entity carrying the gizmo (gizmo picks only).</param>
/// <param name="Handle">The picked handle identity (gizmo picks only).</param>
public readonly record struct EntityIdDecodeResult(
    EntityIdPickKind Kind,
    uint WorldId,
    uint EntityId,
    uint InstanceId,
    uint PrimitiveId,
    uint OwningEntityId,
    GizmoHandleId Handle
);
