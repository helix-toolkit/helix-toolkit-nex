namespace HelixToolkit.Nex.Rendering;

public static class RenderGraphBufferNames
{
    public const string PerFrame = "PerFrame";
    public const string PerDraw = "PerDraw";
    public const string PerMesh = "PerMesh";
    public const string PerMaterial = "PerMaterial";
    public const string PerInstance = "PerInstance";
    public const string PerCamera = "PerCamera";
    public const string PerLight = "PerLight";

    public const string TextureDepth = "TextureDepth";
    public const string TextureMeshId = "TextureMeshId";
    public const string TextureColorF16 = "TextureColorF16";
    public const string TextureOutput = "TextureOutput";

    public const string MeshDrawsOpaque = "MeshDrawsOpaque";
    public const string MeshDrawsTransparent = "MeshDrawsTransparent";
    public const string ForwardPlusConstants = "ForwardPlusConstants";
}

public sealed class RenderGraphBuffers
{
    public TextureHandle TextureDepth = TextureHandle.Null;
    public TextureHandle TextureMeshId = TextureHandle.Null;
    public TextureHandle TextureColorF16 = TextureHandle.Null;
    public TextureHandle TextureOutput = TextureHandle.Null;
    public BufferHandle MeshDrawsOpaque = BufferHandle.Null;
    public BufferHandle MeshDrawsTransparent = BufferHandle.Null;
    public BufferHandle ForwardPlusConstants = BufferHandle.Null;
}
