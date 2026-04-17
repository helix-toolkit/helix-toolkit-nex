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
    /// Attempts to retrieve mesh picking information at the specified pixel coordinates from the given texture.
    /// </summary>
    /// <remarks>If the specified coordinates are outside the bounds of the texture, the method returns <see
    /// langword="false"/> and all output parameters are set to zero. Output identifiers are only valid if the method
    /// returns <see langword="true"/>.</remarks>
    /// <param name="context">The context used to perform the texture download operation.</param>
    /// <param name="meshIdTexture">The texture containing mesh identification data to sample from.</param>
    /// <param name="textureWidth">The width of the mesh ID texture, in pixels.</param>
    /// <param name="textureHeight">The height of the mesh ID texture, in pixels.</param>
    /// <param name="x">The x-coordinate of the pixel to sample, in texture space. Must be greater than or equal to 0 and less than
    /// <paramref name="textureWidth"/>.</param>
    /// <param name="y">The y-coordinate of the pixel to sample, in texture space. Must be greater than or equal to 0 and less than
    /// <paramref name="textureHeight"/>.</param>
    /// <param name="worldId">When this method returns, contains the world identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="entityId">When this method returns, contains the entity identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="instanceId">When this method returns, contains the instance identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="primitiveId">When this method returns, contains the primitive identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <returns><see langword="true"/> if picking information was successfully retrieved and valid identifiers were found;
    /// otherwise, <see langword="false"/>.</returns>
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
    /// <summary>
    /// Attempts to retrieve picking information for a specified screen coordinate from the current render context.
    /// </summary>
    /// <remarks>This method returns false if the picking texture is unavailable or empty. Output parameters
    /// are set to zero if picking fails.</remarks>
    /// <param name="context">The render context containing the resources and state used for picking.</param>
    /// <param name="textureWidth">The width, in pixels, of the picking texture to sample from.</param>
    /// <param name="textureHeight">The height, in pixels, of the picking texture to sample from.</param>
    /// <param name="x">The x-coordinate, in pixels, of the screen position to pick.</param>
    /// <param name="y">The y-coordinate, in pixels, of the screen position to pick.</param>
    /// <param name="worldId">When this method returns, contains the identifier of the world at the specified coordinate, if picking succeeds;
    /// otherwise, zero.</param>
    /// <param name="entityId">When this method returns, contains the identifier of the entity at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <param name="instanceId">When this method returns, contains the identifier of the instance at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <param name="primitiveId">When this method returns, contains the identifier of the primitive at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <returns>true if picking information was successfully retrieved for the specified coordinate; otherwise, false.</returns>
    public static bool TryPick(
        this RenderContext context,
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
            (uint)context.WindowSize.Width,
            (uint)context.WindowSize.Height,
            x,
            y,
            out worldId,
            out entityId,
            out instanceId,
            out primitiveId
        );
    }
}
