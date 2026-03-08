namespace HelixToolkit.Nex.Engine;

public static class Utils
{
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
