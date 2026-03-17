namespace HelixToolkit.Nex.Engine;

public static class Utils
{
    /// <summary>
    /// Extracts the entity ID, entity version, and instance index from the given packed values.
    /// This is the inverse of the packing logic used in the shader(HeaderVertex.glsl), where the entity ID and part of the instance index
    /// </summary>
    /// <param name="r">The packed value containing the entity ID and part of the instance index.</param>
    /// <param name="g">The packed value containing the entity version and part of the instance index.</param>
    /// <param name="entityId">When this method returns, contains the extracted entity ID.</param>
    /// <param name="entityVersion">When this method returns, contains the extracted entity version.</param>
    /// <param name="instanceIndex">When this method returns, contains the extracted instance index.</param>
    public static void UnpackEntityId(
        uint r,
        uint g,
        out int entityId,
        out ushort entityVersion,
        out uint instanceIndex
    )
    {
        entityId = (int)(r & 0xFFFFFF);
        instanceIndex = (r >> 24) | ((g & 0xFFFF) << 8);
        entityVersion = (ushort)(g >> 16);
    }

    public static bool TryPick(
        this IContext context,
        TextureHandle meshIdTexture,
        uint textureWidth,
        uint textureHeight,
        int x,
        int y,
        out int entityId,
        out ushort entityVersion,
        out uint instanceIndex
    )
    {
        entityId = 0;
        entityVersion = 0;
        instanceIndex = 0;
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
                UnpackEntityId(
                    data[0],
                    data[1],
                    out entityId,
                    out entityVersion,
                    out instanceIndex
                );
                return true;
            }
        }

        return false;
    }
}
