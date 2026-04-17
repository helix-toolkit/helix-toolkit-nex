namespace HelixToolkit.Nex.Engine;

public static class Utils
{
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
        worldId = r & 0xF;
        entityId = (r >> 4) & Limits.MaxEntityId;

        // Reconstruct Instance ID (12 bits from X, 10 bits from Y)
        uint instLow = (r >> 20);
        uint instHigh = (g & 0x3FF);
        instanceId = instLow | (instHigh << 12);

        // Extract Primitive ID (Top 22 bits of Y)
        primitiveId = (g >> 10);
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
}
