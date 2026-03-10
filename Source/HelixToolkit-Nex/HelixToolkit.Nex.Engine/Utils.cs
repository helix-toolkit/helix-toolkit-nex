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
        out int entityVersion,
        out uint instanceIndex
    )
    {
        entityId = (int)(r & 0xFFFFFF);
        instanceIndex = (r >> 24) | ((g & 0xFFF) << 8);
        entityVersion = (int)(g >> 12);
    }
}
