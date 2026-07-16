namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Rendering-side packer for the gizmo entity-id encoding written across the R and G channels of
/// the shared <c>TextureEntityId</c> (<c>RG_F32</c>) picking target.
/// </summary>
/// <remarks>
/// <para>
/// The layout must stay byte-for-byte in lock-step with <c>GizmoEncodingConstants</c> and
/// <c>Utils.UnpackGizmoInfo</c> in <c>HelixToolkit.Nex.Engine</c>, which decodes these words:
/// </para>
/// <list type="table">
///   <item><description>R[0..3]   world id = 0 (discriminator trigger)</description></item>
///   <item><description>R[4..7]   encoding type = <c>Gizmo</c> (1)</description></item>
///   <item><description>R[8..31]  owning gizmo entity id (24 bits)</description></item>
///   <item><description>G[0..7]   <c>GizmoAxis</c> (handle id, low byte)</description></item>
///   <item><description>G[8..15]  <c>GizmoMode</c> (handle id, high byte)</description></item>
///   <item><description>G[16..31] reserved = 0 (future per-handle data)</description></item>
/// </list>
/// </remarks>
public static class GizmoPickEncoding
{
    /// <summary>The <c>Gizmo</c> encoding-type value (matches <c>EntityIdEncoding.Gizmo</c> in the Engine).</summary>
    public const uint GizmoEncodingType = (uint)SysEncoding.SysEncodingKind.Gizmo;

    /// <summary>
    /// Shift of the encoding-type field within R. Equals <c>LimitsShaderConstants.WorldIdBits</c>
    /// (4); asserted against it in the static constructor so the two stay in lock-step.
    /// </summary>
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

    /// <summary>
    /// Verifies the mirrored bit layout still matches the engine's world-id field width. If the
    /// world-id bit width ever changes, this fails fast at type initialization rather than silently
    /// producing pick words the engine decode can no longer read.
    /// </summary>
    static GizmoPickEncoding()
    {
        if (EncodingTypeShift != LimitsShaderConstants.WorldIdBits)
        {
            throw new InvalidOperationException(
                $"GizmoPickEncoding.EncodingTypeShift ({EncodingTypeShift}) must equal "
                    + $"LimitsShaderConstants.WorldIdBits ({LimitsShaderConstants.WorldIdBits}); "
                    + "the gizmo pick encoding is out of sync with the world-id field width."
            );
        }
    }

    /// <summary>
    /// Packs a gizmo pick (owning entity id + handle identity) into the R and G channels of the
    /// shared entity-id target. The R channel carries a world id of zero (the alternate-encoding
    /// discriminator trigger), the <c>Gizmo</c> encoding-type field, and the owning gizmo entity id;
    /// the G channel carries the <see cref="GizmoHandleId"/> (axis in the low byte, mode in the next
    /// byte), leaving the high G bits reserved for future per-handle data.
    /// </summary>
    /// <param name="owningEntityId">Id of the entity carrying the gizmo (masked to <see cref="OwningEntityMask"/>).</param>
    /// <param name="handle">The picked handle identity (mode + axis).</param>
    /// <param name="r">Packed R channel output.</param>
    /// <param name="g">Packed G channel output.</param>
    public static void Pack(uint owningEntityId, GizmoHandleId handle, out uint r, out uint g)
    {
        // World id is left at zero (the discriminator trigger); the encoding-type field and the
        // owning entity id occupy the bits above it.
        r =
            (GizmoEncodingType << EncodingTypeShift)
            | ((owningEntityId & OwningEntityMask) << OwningEntityShift);

        g =
            (((uint)handle.Axis & HandleFieldMask) << AxisShift)
            | (((uint)handle.Mode & HandleFieldMask) << ModeShift);
    }
}
