namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Declares the broad execution phase a render/compute pass belongs to.
/// The graph compiler automatically orders passes so that all passes in an
/// earlier stage complete before any pass in a later stage begins, without
/// requiring every node to name its predecessors explicitly.
/// <para>
/// Within a single stage, fine-grained ordering is still expressed through
/// resource edges (inputs/outputs) or the explicit <c>after</c> list.
/// </para>
/// </summary>
public enum RenderStage : uint
{
    /// <summary>CPU/GPU data preparation: frustum culling etc.</summary>
    Prepare = 0,

    /// <summary>Opaque geometry: depth pre-pass, light culling, opaque meshes, point clouds, etc.</summary>
    Opaque,

    /// <summary>
    ///  FXAA or other full-screen anti-aliasing pass. 
    /// </summary>
    Antialising,

    /// <summary>
    /// Particle rendering.
    /// </summary>
    Particle,

    /// <summary>Transparent geometry: WBOIT render + composite, alpha-blended passes, etc.</summary>
    Transparent,

    /// <summary>Post processing effects.</summary>
    PostProcess,

    Bloom,
    /// <summary>
    /// Billboard rendering. Placed after bloom to avoid unncessary bloom.
    /// </summary>
    Billboard,
    /// <summary>
    /// HDR-to-LDR conversion. Separating this from <see cref="PostProcess"/> ensures that
    /// all HDR effects complete before the scene is linearised, and that all
    /// <see cref="Overlay"/> passes receive an LDR surface to draw onto.
    /// </summary>
    ToneMap,

    /// <summary>
    /// LDR overlays rendered on top of the tone-mapped image: gizmos, debug geometry,
    /// editor widgets, etc. Depth buffer from the opaque pass is still available here.
    /// </summary>
    Overlay,

    /// <summary>Final blit to the swap-chain / output texture.</summary>
    Output,

    /// <summary>
    /// Sentinel value for iteration and validation — not an actual stage.
    /// </summary>
    StageCount,

    /// <summary>
    /// Used in renderer to indicate a command buffer submission operation. Not an actual render stage, and not processed by the graph compiler.
    /// </summary>
    SubmitFlag
}

public static class RenderStageNames
{
    public static readonly FastList<(byte[] Name, Color4 Color)> Names = [];

    static RenderStageNames()
    {
        for (int i = 0; i < (int)RenderStage.StageCount; i++)
        {
            var name = ((RenderStage)i).ToString();
            Names.Add((System.Text.Encoding.UTF8.GetBytes(name), GetColorForStage((RenderStage)i)));
        }
        Names.Add((System.Text.Encoding.UTF8.GetBytes(RenderStage.StageCount.ToString()), GetColorForStage(RenderStage.StageCount)));
        Names.Add((System.Text.Encoding.UTF8.GetBytes(RenderStage.SubmitFlag.ToString()), GetColorForStage(RenderStage.SubmitFlag)));
    }

    private static Color4 GetColorForStage(RenderStage i)
    {
        switch (i)
        {
            case RenderStage.Prepare:
                return new Color4(1, 0, 0, 1);
            case RenderStage.Opaque:
                return new Color4(0, 1, 0, 1);
            case RenderStage.Antialising:
                return new Color4(0, 0, 1, 1);
            case RenderStage.Particle:
                return new Color4(1, 1, 0, 1);
            case RenderStage.Transparent:
                return new Color4(0, 1, 1, 1);
            case RenderStage.PostProcess:
                return new Color4(1, 0, 1, 1);
            case RenderStage.Bloom:
                return new Color4(0.5f, 0.5f, 0.5f, 1);
            case RenderStage.Billboard:
                return new Color4(0.5f, 0, 0, 1);
            case RenderStage.ToneMap:
                return new Color4(0, 0.5f, 0, 1);
            case RenderStage.Overlay:
                return new Color4(0, 0, 0.5f, 1);
            case RenderStage.Output:
                return new Color4(0.5f, 0.5f, 0, 1);
            case RenderStage.StageCount:
                return new Color4(0, 0.5f, 0.5f, 1);
            case RenderStage.SubmitFlag:
                return new Color4(0.5f, 0, 0.5f, 1);
            default:
                return new Color4(1, 1, 1, 1);
        }
    }
}
