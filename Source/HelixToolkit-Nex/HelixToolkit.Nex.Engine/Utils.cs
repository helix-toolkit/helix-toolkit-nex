using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine;



public static class Utils
{
    /// <summary>
    /// Packs world ID, entity ID, instance index, and primitive ID into two 32-bit unsigned integers.
    /// This is the C# equivalent of the GLSL <c>packObjectInfo</c> + <c>packPrimitiveId</c> functions.
    /// </summary>
    /// <param name="worldId">World ID (0 to <see cref="Limits.MaxWorldId"/>).</param>
    /// <param name="entityId">Entity ID (0 to <see cref="Limits.MaxEntityId"/>).</param>
    /// <param name="instanceIndex">Instance index (0 to <see cref="Limits.MaxInstanceCount"/>).</param>
    /// <param name="primitiveId">Primitive ID (0 to <see cref="Limits.MaxPrimitiveCount"/>).</param>
    /// <param name="r">Packed X channel output.</param>
    /// <param name="g">Packed Y channel output.</param>
    public static void PackMeshInfo(
        uint worldId,
        uint entityId,
        uint instanceIndex,
        uint primitiveId,
        out uint r,
        out uint g
    )
    {
        r =
            (worldId & LimitsShaderConstants.WorldIdMask)
            | (
                (entityId & LimitsShaderConstants.EntityIdMask)
                << LimitsShaderConstants.EntityIdShift
            )
            | (
                (instanceIndex & LimitsShaderConstants.InstanceLowMask)
                << LimitsShaderConstants.InstanceLowShift
            );

        g =
            (
                (instanceIndex >> LimitsShaderConstants.InstanceLowBits)
                & LimitsShaderConstants.InstanceHighMask
            )
            | (
                (primitiveId & LimitsShaderConstants.IndexCountMask)
                << LimitsShaderConstants.PrimitiveIdShift
            );
    }

    /// <summary>
    /// Unpacks the mesh information encoded in the red and green channels of a texture pixel.
    /// </summary>
    /// <param name="r"></param>
    /// <param name="g"></param>
    /// <param name="worldId"></param>
    /// <param name="entityId"></param>
    /// <param name="instanceId"></param>
    /// <param name="primitiveId"></param>
    public static void UnpackMeshInfo(
        uint r,
        uint g,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        // Extract World and Entity
        worldId = r & LimitsShaderConstants.WorldIdMask;
        entityId = (r >> LimitsShaderConstants.EntityIdShift) & LimitsShaderConstants.EntityIdMask;

        // Reconstruct Instance ID
        uint instLow = (r >> LimitsShaderConstants.InstanceLowShift);
        uint instHigh = (g & LimitsShaderConstants.InstanceHighMask);
        instanceId = instLow | (instHigh << LimitsShaderConstants.InstanceLowBits);

        // Extract Primitive ID
        primitiveId = (g >> LimitsShaderConstants.PrimitiveIdShift);
    }

    /// <summary>
    /// Unpacks mesh information from a single 64-bit unsigned integer, where the lower 32 bits represent the red channel and the upper 32 bits represent the green channel.
    /// </summary>
    /// <param name="id">The 64-bit packed mesh information.</param>
    /// <param name="worldId">The unpacked world ID.</param>
    /// <param name="entityId">The unpacked entity ID.</param>
    /// <param name="instanceId">The unpacked instance ID.</param>
    /// <param name="primitiveId">The unpacked primitive ID.</param>
    public static void UnpackMeshInfo(
        ulong id,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        uint r = (uint)(id & 0xFFFFFFFF);
        uint g = (uint)(id >> 32);
        UnpackMeshInfo(r, g, out worldId, out entityId, out instanceId, out primitiveId);
    }

    /// <summary>
    /// Packs a gizmo pick (owning entity id + handle identity) into the R and G channels of the
    /// shared <c>TextureEntityId</c> target. The R channel carries a world id of zero (the
    /// alternate-encoding discriminator trigger), the <see cref="EntityIdEncoding.Gizmo"/>
    /// encoding-type field, and the owning gizmo entity id; the G channel carries the
    /// <see cref="GizmoHandleId"/> (axis in the low byte, mode in the next byte), leaving the high
    /// G bits reserved for future per-handle data.
    /// </summary>
    /// <param name="owningEntityId">Id of the entity carrying the gizmo (masked to <see cref="GizmoEncodingConstants.OwningEntityMask"/>).</param>
    /// <param name="handle">The picked handle identity (mode + axis).</param>
    /// <param name="r">Packed R channel output.</param>
    /// <param name="g">Packed G channel output.</param>
    public static void PackGizmoInfo(
        uint owningEntityId,
        GizmoHandleId handle,
        out uint r,
        out uint g
    )
    {
        r =
            (0u & LimitsShaderConstants.WorldIdMask) // worldId = 0 (discriminator trigger)
            | ((uint)EntityIdEncoding.Gizmo << GizmoEncodingConstants.EncodingTypeShift)
            | (
                (owningEntityId & GizmoEncodingConstants.OwningEntityMask)
                << GizmoEncodingConstants.OwningEntityShift
            );

        g =
            (
                ((uint)handle.Axis & GizmoEncodingConstants.HandleFieldMask)
                << GizmoEncodingConstants.AxisShift
            )
            | (
                ((uint)handle.Mode & GizmoEncodingConstants.HandleFieldMask)
                << GizmoEncodingConstants.ModeShift
            );
    }

    /// <summary>
    /// Unpacks a gizmo pick previously packed by <see cref="PackGizmoInfo"/>, recovering the owning
    /// gizmo entity id from the R channel and the <see cref="GizmoHandleId"/> from the G channel.
    /// This is the exact inverse of <see cref="PackGizmoInfo"/>.
    /// </summary>
    /// <param name="r">Packed R channel.</param>
    /// <param name="g">Packed G channel.</param>
    /// <param name="owningEntityId">The unpacked owning gizmo entity id.</param>
    /// <param name="handle">The unpacked handle identity (mode + axis).</param>
    public static void UnpackGizmoInfo(
        uint r,
        uint g,
        out uint owningEntityId,
        out GizmoHandleId handle
    )
    {
        owningEntityId =
            (r >> GizmoEncodingConstants.OwningEntityShift) & GizmoEncodingConstants.OwningEntityMask;

        var axis = (GizmoAxis)(
            (g >> GizmoEncodingConstants.AxisShift) & GizmoEncodingConstants.HandleFieldMask
        );
        var mode = (GizmoMode)(
            (g >> GizmoEncodingConstants.ModeShift) & GizmoEncodingConstants.HandleFieldMask
        );

        handle = new GizmoHandleId(mode, axis);
    }

    /// <summary>
    /// The single, unified entry point for decoding a pixel of the shared <c>TextureEntityId</c>
    /// target. Inspects the world id first: a decoded world id greater than zero is a scene pick
    /// (decoded by the unchanged <see cref="UnpackMeshInfo(uint, uint, out uint, out uint, out uint, out uint)"/>);
    /// a decoded world id of zero selects an alternate encoding chosen by the
    /// <see cref="EntityIdEncoding"/> discriminator carried in the R channel. The only alternate
    /// encoding defined today is <see cref="EntityIdEncoding.Gizmo"/>. Unrecognized encoding values
    /// (including <see cref="EntityIdEncoding.None"/> and the cleared-to-<c>(0, 0)</c> pixel) decode
    /// to <see cref="EntityIdPickKind.NoHit"/>. This method never throws.
    /// </summary>
    /// <param name="r">The raw R channel bits.</param>
    /// <param name="g">The raw G channel bits.</param>
    /// <returns>The fully-decoded <see cref="EntityIdDecodeResult"/>.</returns>
    public static EntityIdDecodeResult UnpackEntityId(uint r, uint g)
    {
        uint worldId = r & LimitsShaderConstants.WorldIdMask;
        if (worldId > 0)
        {
            // Scene pick: decode via the unchanged mesh-info path (never a gizmo).
            UnpackMeshInfo(r, g, out var w, out var e, out var inst, out var prim);
            return new EntityIdDecodeResult(
                EntityIdPickKind.Scene,
                w,
                e,
                inst,
                prim,
                0,
                default
            );
        }

        // worldId == 0: select the alternate encoding from the encoding-type field.
        uint encoding =
            (r >> LimitsShaderConstants.WorldIdBits) & GizmoEncodingConstants.EncodingTypeMask;
        if (encoding == (uint)EntityIdEncoding.Gizmo)
        {
            UnpackGizmoInfo(r, g, out uint owningEntityId, out GizmoHandleId handle);
            return new EntityIdDecodeResult(
                EntityIdPickKind.Gizmo,
                0,
                0,
                0,
                0,
                owningEntityId,
                handle
            );
        }

        // Unrecognized encoding (including None / cleared pixel) -> no hit, no exception.
        return new EntityIdDecodeResult(EntityIdPickKind.NoHit, 0, 0, 0, 0, 0, default);
    }

    /// <summary>
    /// Decodes a pixel of the shared <c>TextureEntityId</c> target from a single 64-bit packed
    /// value, where the lower 32 bits are the R channel and the upper 32 bits are the G channel.
    /// This mirrors <see cref="UnpackMeshInfo(ulong, out uint, out uint, out uint, out uint)"/> for
    /// picking callers that read a packed <see cref="ulong"/>.
    /// </summary>
    /// <param name="id">The 64-bit packed pixel (low 32 = R, high 32 = G).</param>
    /// <returns>The fully-decoded <see cref="EntityIdDecodeResult"/>.</returns>
    public static EntityIdDecodeResult UnpackEntityId(ulong id)
    {
        uint r = (uint)(id & 0xFFFFFFFF);
        uint g = (uint)(id >> 32);
        return UnpackEntityId(r, g);
    }
}
