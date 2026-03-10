namespace HelixToolkit.Nex.Rendering;

public static class RenderGraphExtensions
{
    /// <summary>
    /// Applies a sub-graph configurator to this render graph, enabling composition of
    /// reusable rendering stages without creating separate <see cref="RenderGraph"/> instances.
    /// </summary>
    /// <param name="graph">The render graph to configure.</param>
    /// <param name="configurator">
    /// A delegate that receives the graph and registers its passes and resources into it.
    /// </param>
    /// <returns>The same <see cref="RenderGraph"/> instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// var graph = new RenderGraph(services)
    ///     .Apply(DepthPrepassConfigurator.AddTo)
    ///     .Apply(ForwardPlusLightCullingConfigurator.AddTo);
    /// </code>
    /// </example>
    public static RenderGraph Apply(this RenderGraph graph, Action<RenderGraph> configurator)
    {
        configurator(graph);
        return graph;
    }
}
