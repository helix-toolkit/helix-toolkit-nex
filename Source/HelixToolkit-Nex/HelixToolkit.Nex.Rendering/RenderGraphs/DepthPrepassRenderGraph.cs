namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// A render graph that performs frustum culling, a depth/mesh-id pre-pass, optional debug
/// visualizations, and a final post-effects pass.
/// <para>
/// Stage order:
/// <list type="number">
///   <item><see cref="DepthPrepassConfigurator"/> — cull + depth + mesh-id</item>
///   <item><see cref="DebugVisualizationConfigurator"/> — optional debug views (depth, mesh-id)</item>
///   <item><see cref="PostEffectsConfigurator"/> — tone-mapping → swapchain</item>
/// </list>
/// </para>
/// </summary>
public static class DepthPrepassRenderGraph
{
    public static RenderGraph Create(IServiceProvider serviceProvider) =>
        new RenderGraph(serviceProvider)
            .Apply(DepthPrepassConfigurator.AddTo)
            .Apply(DebugVisualizationConfigurator.AddTo)
            .Apply(PostEffectsConfigurator.AddTo);
}
