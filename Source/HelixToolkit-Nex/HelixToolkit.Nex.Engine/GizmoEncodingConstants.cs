namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Bit-layout constants for the gizmo entity-id encoding written across the R and G
/// channels of the shared <c>TextureEntityId</c> (<c>RG_F32</c>) picking target.
/// </summary>
/// <remarks>
/// <para>
/// The R channel low bits carry the world id (<see cref="LimitsShaderConstants.WorldIdBits"/>);
/// a world id of zero triggers the alternate-encoding path. The encoding-type field
/// immediately follows the world id, then the owning gizmo entity id occupies the
/// remaining high R bits. The G channel carries the <c>GizmoHandleId</c>: the axis in
/// the low byte and the mode in the next byte, leaving the high G bits reserved for
/// future per-handle data.
/// </para>
/// <para>Layout:</para>
/// <list type="table">
///   <item><description>R[0..3]   world id = 0 (discriminator trigger)</description></item>
///   <item><description>R[4..7]   encoding type = <c>Gizmo</c></description></item>
///   <item><description>R[8..31]  owning gizmo entity id (24 bits)</description></item>
///   <item><description>G[0..7]   <c>GizmoAxis</c> (handle id, low byte)</description></item>
///   <item><description>G[8..15]  <c>GizmoMode</c> (handle id, high byte)</description></item>
///   <item><description>G[16..31] reserved = 0 (future per-handle data)</description></item>
/// </list>
/// </remarks>
public static class GizmoEncodingConstants
{
    /// <summary>Number of R bits used for the encoding-type discriminator (15 future encodings besides <c>None</c>).</summary>
    public const int EncodingTypeBits = 4;

    /// <summary>Mask isolating the encoding-type field (<c>0xF</c>).</summary>
    public const uint EncodingTypeMask = (1u << EncodingTypeBits) - 1u;

    /// <summary>Shift of the encoding-type field within R (== <see cref="LimitsShaderConstants.WorldIdBits"/>).</summary>
    public const int EncodingTypeShift = 4;

    /// <summary>Shift of the owning gizmo entity id within R (<c>WorldIdBits + EncodingTypeBits</c>).</summary>
    public const int OwningEntityShift = 8;

    /// <summary>Mask for the owning gizmo entity id (24 bits available in R).</summary>
    public const uint OwningEntityMask = (1u << 24) - 1u;

    /// <summary>Shift of the <c>GizmoAxis</c> field within G.</summary>
    public const int AxisShift = 0;

    /// <summary>Shift of the <c>GizmoMode</c> field within G.</summary>
    public const int ModeShift = 8;

    /// <summary>Per-field mask for the handle id fields packed into G (one byte each).</summary>
    public const uint HandleFieldMask = 0xFFu;
}
