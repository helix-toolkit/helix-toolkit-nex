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

    public static bool TryPick(
        this IContext context,
        TextureHandle meshIdTexture,
        uint textureWidth,
        uint textureHeight,
        int x,
        int y,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        worldId = 0;
        entityId = 0;
        instanceId = 0;
        primitiveId = 0;
        if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight)
        {
            return false;
        }
        unsafe
        {
            var data = stackalloc uint[2];
            var ret = context
                .Download(
                    meshIdTexture,
                    new TextureRangeDesc()
                    {
                        Dimensions = new Dimensions(1, 1, 1),
                        Offset = new Offset3D(x, y),
                    },
                    (nint)data,
                    sizeof(ulong)
                )
                .CheckResult();
            if (ret == ResultCode.Ok)
            {
                UnpackMeshInfo(
                    data[0],
                    data[1],
                    out worldId,
                    out entityId,
                    out instanceId,
                    out primitiveId
                );
                return worldId > 0 && entityId > 0;
            }
        }

        return false;
    }

    public static bool TryPick(
        this RenderContext context,
        uint textureWidth,
        uint textureHeight,
        int x, int y,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        worldId = 0;
        entityId = 0;
        instanceId = 0;
        primitiveId = 0;
        if (!context.ResourceSet.Textures.TryGetValue(SystemBufferNames.TextureEntityId, out var texture) || texture.Empty)
        {
            return false;
        }
        return TryPick(
            context.Context,
            texture,
            textureWidth,
            textureHeight,
            x,
            y,
            out worldId,
            out entityId,
            out instanceId,
            out primitiveId
        );
    }
}
