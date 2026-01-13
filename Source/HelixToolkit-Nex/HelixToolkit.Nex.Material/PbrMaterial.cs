namespace HelixToolkit.Nex.Material;

/// <summary>
/// Concrete PBR material implementation with automatic shader generation.
/// </summary>
[MaterialName("PBR")]
public class PbrMaterial : Material<PbrMaterialProperties>
{
    private RenderPipelineResource _cachedPipeline = RenderPipelineResource.Null;
    private IContext? _context;

    /// <summary>
    /// Gets the cached render pipeline for this material.
    /// Pipeline is created lazily when first accessed.
    /// </summary>
    public override RenderPipelineResource Pipeline => _cachedPipeline;

    /// <summary>
    /// Initialize or update the material's render pipeline.
    /// </summary>
    /// <param name="context">Graphics context for creating resources.</param>
    /// <param name="pipelineDesc">Base pipeline description (shaders will be generated).</param>
    /// <returns>True if pipeline was successfully created/updated.</returns>
    public bool InitializePipeline(IContext context, RenderPipelineDesc pipelineDesc)
    {
        _context = context;

        // Build shaders based on material properties
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .ForMaterial(Properties);

        var result = builder.BuildMaterialPipeline(context, Properties.DebugName ?? "PbrMaterial");

        if (!result.Success)
        {
            return false;
        }

        // Update pipeline descriptor with generated shaders
        pipelineDesc.VertexShader = result.VertexShader;
        pipelineDesc.FragementShader = result.FragmentShader;
        pipelineDesc.DebugName = Properties.DebugName ?? "PbrMaterial_Pipeline";

        // Create the pipeline
        _cachedPipeline = context.CreateRenderPipeline(pipelineDesc);

        return _cachedPipeline.Valid;
    }

    /// <summary>
    /// Invalidate the cached pipeline so it will be regenerated on next use.
    /// Call this when material properties change significantly (e.g., textures added/removed).
    /// </summary>
    public void InvalidatePipeline()
    {
        _cachedPipeline.Reset();
    }
}
