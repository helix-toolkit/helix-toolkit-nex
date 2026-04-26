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

    public const string BufferMeshInfo = "BufMeshInfo";

    /// <summary>
    /// RGBA16F accumulation texture for Weighted Blended Order-Independent Transparency (WBOIT).
    /// Stores premultiplied-alpha weighted color: <c>vec4(color.rgb * w, alpha * w)</c>.
    /// </summary>
    public const string TextureWboitAccum = "TexWboitAccum";

    /// <summary>
    /// R16F revealage texture for Weighted Blended Order-Independent Transparency (WBOIT).
    /// Initialized to 1 and multiplicatively reduced by each transparent fragment's <c>(1 - alpha)</c>.
    /// </summary>
    public const string TextureWboitRevealage = "TexWboitReveal";
    public const string BufferLightGrid = "BufLightGrid";
    public const string BufferLightIndex = "BufLightIndex";
    public const string BufferDirectionalLight = "BufDirLight";
    public const string BufferLights = "BufLights";
    public const string BufferPBRProperties = "BufPBRProps";

    /// <summary>
    /// Intermediate texture A used by the <see cref="PostEffects.Bloom"/> effect for the
    /// brightness-extract output and the final blurred result.
    /// Allocated at a reduced resolution determined by <c>Bloom.DownsampleFactor</c>
    /// (default: quarter of screen size).
    /// </summary>
    public const string TextureBloomA = "TexBloomA";

    /// <summary>
    /// Intermediate texture B used by the <see cref="PostEffects.Bloom"/> effect as the
    /// horizontal-blur target during the ping-pong blur passes.
    /// Allocated at a reduced resolution determined by <c>Bloom.DownsampleFactor</c>
    /// (default: quarter of screen size).
    /// </summary>
    public const string TextureBloomB = "TexBloomB";

    /// <summary>
    /// Single-channel (R8) silhouette mask written by
    /// <see cref="PostEffects.BorderHighlightPostEffect"/> during its first pass.
    /// White pixels mark pixels covered by a highlighted mesh; black pixels are
    /// background.  The effect's composite pass reads this texture to detect and
    /// draw the outline.
    /// </summary>
    public const string TextureHighlightMask = "TexHighlightMask";

    /// <summary>
    /// Two-channel (RG8) edge mask written by the SMAA edge-detection pass.
    /// R = horizontal edge, G = vertical edge.  Allocated at full screen resolution.
    /// </summary>
    public const string TextureSmaaEdges = "TexSmaaEdges";

    /// <summary>
    /// Four-channel (RGBA8) blending-weight texture written by the SMAA
    /// blending-weight pass.  Stores per-pixel MLAA blend weights in the
    /// (left, right, top, bottom) layout.  Allocated at full screen resolution.
    /// </summary>
    public const string TextureSmaaWeights = "TexSmaaWeights";

    /// <summary>
    /// GPU buffer holding <c>PointDrawData</c> structs written by the point expansion
    /// compute shader and read by the point vertex shader.
    /// </summary>
    public const string BufferPointDrawData = "BufPointDrawData";

    /// <summary>
    /// GPU buffer holding <c>PointDrawIndirectArgs</c> for the point rendering draw call.
    /// The compute shader atomically increments <c>instanceCount</c>.
    /// </summary>
    public const string BufferPointIndirectArgs = "BufPointIndirectArgs";
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
