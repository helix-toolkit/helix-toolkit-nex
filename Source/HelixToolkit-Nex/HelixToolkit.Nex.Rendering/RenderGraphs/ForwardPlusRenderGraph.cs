namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// A render graph that chains the full Forward+ rendering pipeline:
/// frustum culling → depth pre-pass → tiled light culling → post-effects.
/// <para>
/// Stage order:
/// <list type="number">
///   <item><see cref="DepthPrepassConfigurator"/> — cull + depth + mesh-id</item>
///   <item><see cref="ForwardPlusLightCullingConfigurator"/> — tiled Forward+ light culling</item>
///   <item><see cref="PostEffectsConfigurator"/> — tone-mapping → swapchain</item>
/// </list>
/// </para>
/// </summary>
public static class ForwardPlusRenderGraph
{
    public static RenderGraph Create(IServiceProvider serviceProvider) =>
        new RenderGraph(serviceProvider)
            .Apply(DepthPrepassConfigurator.AddTo)
            .Apply(ForwardPlusLightCullingConfigurator.AddTo)
            .Apply(PostEffectsConfigurator.AddTo);
}
