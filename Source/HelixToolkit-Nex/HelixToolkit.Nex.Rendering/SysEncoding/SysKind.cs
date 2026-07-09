namespace HelixToolkit.Nex.Rendering.SysEncoding;


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
public enum SysEncodingKind : uint
{
    /// <summary>Reserved: unrecognized / cleared pixel -&gt; No hit.</summary>
    None = 0,

    /// <summary>Gizmo pick encoding.</summary>
    Gizmo = 1,

    // Future encodings append here without changing world-id-based discrimination.
}
