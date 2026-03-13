namespace HelixToolkit.Nex.Rendering;

public static class SystemBufferNames
{
    public const string FinalOutputTexture = "FinalOutputTex";
    public const string BufferForwardPlusConstants = "BufFPConst";

    public const string TextureDepthF32 = "TexDepthF32";
    public const string TextureEntityId = "TexEntityId";
    public const string TextureColorF16A = "TexColorF16A";
    public const string TextureColorF16B = "TexColorF16B";

    /// <summary>
    /// A stable logical slot that always resolves to the physical texture containing the most
    /// recently completed color output. <see cref="RenderNodes.PostEffectsNode"/> updates this
    /// alias at the end of its render loop so that downstream passes (e.g. tone mapping to the
    /// final output) can read the correct texture regardless of how many post-effects ran.
    /// </summary>
    public const string TextureColorF16Current = "TexColorF16Current";

    public const string BufferMeshDrawOpaque = "BufMeshDrawOpaque";
    public const string BufferMeshDrawTransparent = "BufMeshDrawTrans";
    public const string BufferLightGrid = "BufLightGrid";
    public const string BufferLightIndex = "BufLightIndex";
    public const string BufferDirectionalLight = "BufDirLight";
    public const string BufferLights = "BufLights";
}

/// <summary>
/// Well-known names for ping-pong texture groups registered with <see cref="RenderGraph.AddPingPongGroup"/>.
/// </summary>
public static class PingPongGroups
{
    /// <summary>
    /// The main HDR color ping-pong pair: <see cref="SystemBufferNames.TextureColorF16A"/>
    /// and <see cref="SystemBufferNames.TextureColorF16B"/>.
    /// </summary>
    public const string ColorF16 = "PingPong_ColorF16";
}
