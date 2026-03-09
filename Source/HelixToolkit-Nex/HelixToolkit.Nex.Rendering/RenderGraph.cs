namespace HelixToolkit.Nex.Rendering;

public readonly record struct RenderResource(string Name, ResourceType Type);

public sealed record ResourceBuildParams(RenderContext Context);

public sealed class RenderGraph(IServiceProvider serviceProvider) : Initializable
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(RenderGraph));
    private static readonly ILogger _logger = LogManager.Create<RenderGraph>();

    public sealed class GraphNode(
        string passName,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs,
        Action<RenderResources> onSetup
    )
    {
        public readonly RenderPass Pass = new();
        public readonly Framebuffer Framebuffer = new();
        public readonly Dependencies Dependencies = new();
        public readonly string PassName = passName;
        public readonly IList<RenderResource> Inputs = inputs;
        public readonly IList<RenderResource> Outputs = outputs;
        public readonly Action<RenderResources> OnSetupRenderParams = onSetup;
    }

    private readonly IServiceProvider _services = serviceProvider;
    private readonly Dictionary<string, GraphNode> _passes = [];
    private readonly Dictionary<string, List<GraphNode>> _resourceProducers = [];
    private readonly List<GraphNode> _sortedPasses = [];

    public IReadOnlyList<GraphNode> SortedPasses => _sortedPasses;

    public bool IsDirty { private set; get; } = true;

    public override string Name => nameof(RenderGraph);

    /// <summary>
    /// Adds a texture to the render graph with the specified name and build function.
    /// The texture builder is registered on the <see cref="RenderContext.ResourceSet"/>
    /// during <see cref="Execute"/>.
    /// </summary>
    /// <param name="name">The unique name of the texture to add. Must not already exist in the render graph.</param>
    /// <param name="buildFunc">A function that defines how to build the texture resource. Can be <see langword="null"/> if no custom build is
    /// required.</param>
    /// <param name="dependsOnScreenSize">Indicates whether the texture's size should depend on the screen size. The default is <see langword="true"/>.</param>
    /// <returns>The current instance of the <see cref="RenderGraph"/> to allow for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a texture with the specified <paramref name="name"/> and its creation function already exists in the render graph.</exception>
    public RenderGraph AddTexture(
        string name,
        Func<ResourceBuildParams, TextureResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (
            buildFunc is not null
            && _textureRegistrations.TryGetValue(name, out var existing)
            && existing.BuildFunc is not null
        )
        {
            throw new InvalidOperationException(
                $"A texture with the name '{name}' already exists in the render graph."
            );
        }
        _textureRegistrations[name] = new TextureRegistration(buildFunc, dependsOnScreenSize);
        IsDirty = true;
        return this;
    }

    public RenderGraph AddFinalOutputTexture()
    {
        return AddTexture(SystemBufferNames.FinalOutputTexture, null);
    }

    /// <summary>
    /// Adds a buffer to the render graph with the specified name and build function.
    /// The buffer builder is registered on the <see cref="RenderContext.ResourceSet"/>
    /// during <see cref="Execute"/>.
    /// </summary>
    /// <param name="name">The unique name of the buffer to add. Must not already exist in the render graph.</param>
    /// <param name="buildFunc">A function that defines how to build the buffer resource. Can be <see langword="null"/> if no specific build
    /// function is required.</param>
    /// <param name="dependsOnScreenSize">Indicates whether the buffer's size depends on the screen size. The default is <see langword="true"/>.</param>
    /// <returns>The current instance of <see cref="RenderGraph"/> to allow for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a buffer with the specified <paramref name="name"/> and its creation function already exists in the render graph.</exception>
    public RenderGraph AddBuffer(
        string name,
        Func<ResourceBuildParams, BufferResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (
            buildFunc is not null
            && _bufferRegistrations.TryGetValue(name, out var existing)
            && existing.BuildFunc is not null
        )
        {
            throw new InvalidOperationException(
                $"A buffer with the name '{name}' already exists in the render graph."
            );
        }
        _bufferRegistrations[name] = new BufferRegistration(buildFunc, dependsOnScreenSize);
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Adds a new render pass to the render graph with the specified name, input resources, output resources, and setup
    /// action.
    /// </summary>
    /// <param name="passName">The unique name of the render pass to add. Must not already exist in the render graph.</param>
    /// <param name="inputs">A list of input resources required by the render pass.</param>
    /// <param name="outputs">A list of output resources produced by the render pass.</param>
    /// <param name="onSetup">An action to configure the render resources for the pass.</param>
    /// <returns>The current instance of the <see cref="RenderGraph"/> to allow for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a pass with the specified <paramref name="passName"/> already exists in the render graph.</exception>
    public RenderGraph AddPass(
        string passName,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs,
        Action<RenderResources> onSetup
    )
    {
        if (_passes.ContainsKey(passName))
        {
            throw new InvalidOperationException(
                $"A pass with the name '{passName}' already exists in the render graph."
            );
        }
        _passes.Add(passName, new GraphNode(passName, inputs, outputs, onSetup));
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Removes a render pass by its name and resets the internal state of the render graph.
    /// </summary>
    /// <param name="passName">The name of the render pass to remove. Cannot be null or empty.</param>
    /// <returns>The current instance of <see cref="RenderGraph"/> for method chaining.</returns>
    public RenderGraph RemovePass(string passName)
    {
        _passes.Remove(passName);
        _resourceProducers.Clear();
        _sortedPasses.Clear();
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Compiles the render graph by identifying resource producers, building a dependency graph,  and performing a
    /// topological sort of the passes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if a circular dependency is detected in the render graph.</exception>
    public void Compile()
    {
        using var t = _tracer.BeginScope(nameof(Compile));
        _resourceProducers.Clear();
        _sortedPasses.Clear();

        // 1. Identify producers for each resource
        foreach (var pass in _passes)
        {
            foreach (var output in pass.Value.Outputs)
            {
                if (!_resourceProducers.ContainsKey(output.Name))
                {
                    _resourceProducers[output.Name] = [];
                }
                _resourceProducers[output.Name].Add(pass.Value);
            }
        }

        // 2. Build dependency graph and calculate in-degrees
        var inDegree = new Dictionary<GraphNode, int>();
        var adjacencyList = new Dictionary<GraphNode, List<GraphNode>>();

        foreach (var pass in _passes.Values)
        {
            inDegree[pass] = 0;
            adjacencyList[pass] = [];
        }

        // Build edges: if pass B depends on output from pass A, then A -> B.
        var addedEdges = new HashSet<(GraphNode, GraphNode)>();
        foreach (var pass in _passes.Values)
        {
            foreach (var input in pass.Inputs)
            {
                if (!_resourceProducers.TryGetValue(input.Name, out var producers))
                {
                    // No internal producer — treat as an external resource (e.g. swapchain).
                    continue;
                }

                foreach (var producer in producers)
                {
                    // Only add each producer→consumer edge once.
                    if (addedEdges.Add((producer, pass)))
                    {
                        adjacencyList[producer].Add(pass);
                        inDegree[pass]++;
                    }
                }
            }
        }

        // 3. Perform Kahn's algorithm for topological sort
        var queue = new Queue<GraphNode>();

        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
            {
                queue.Enqueue(kvp.Key);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            _sortedPasses.Add(current);

            foreach (var dependent in adjacencyList[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // 4. Check for cycles
        if (_sortedPasses.Count != _passes.Count)
        {
            var unsortedPasses = _passes
                .Values.Where(p => !_sortedPasses.Contains(p))
                .Select(p => p.PassName);
            throw new InvalidOperationException(
                $"Circular dependency detected in render graph. Affected passes: {string.Join(", ", unsortedPasses)}"
            );
        }

        IsDirty = false;
    }

    public void Execute(
        RenderContext context,
        ICommandBuffer cmdBuf,
        IReadOnlyDictionary<string, RenderNode> nodes
    )
    {
        var resourceSet =
            context.ResourceSet
            ?? throw new InvalidOperationException(
                "RenderContext.ResourceSet must be set before executing the render graph."
            );

        var wasDirty = IsDirty;
        if (wasDirty)
        {
            Compile();
            ApplyRegistrations(resourceSet);
        }

        resourceSet.EnsureResources(context, wasDirty);

        SetupResourcesForPass(context, cmdBuf, resourceSet);
        foreach (var pass in _sortedPasses)
        {
            if (!nodes.TryGetValue(pass.PassName, out var node))
            {
                _logger.LogTrace(
                    "No render node found for pass '{PASS}'. Skipping this pass.",
                    pass.PassName
                );
                continue;
            }
            if (!node.Enabled)
            {
                continue;
            }

            node.Render(
                new RenderResources(
                    context,
                    cmdBuf,
                    pass.Pass,
                    pass.Framebuffer,
                    pass.Dependencies,
                    resourceSet.Textures,
                    resourceSet.Buffers
                )
            );
        }
    }

    protected override ResultCode OnInitializing()
    {
        Compile();
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Resource registration storage (deferred until Execute)
    // -----------------------------------------------------------------------

    private readonly record struct TextureRegistration(
        Func<ResourceBuildParams, TextureResource>? BuildFunc,
        bool DependsOnScreenSize
    );

    private readonly record struct BufferRegistration(
        Func<ResourceBuildParams, BufferResource>? BuildFunc,
        bool DependsOnScreenSize
    );

    private readonly Dictionary<string, TextureRegistration> _textureRegistrations = [];
    private readonly Dictionary<string, BufferRegistration> _bufferRegistrations = [];

    /// <summary>
    /// Forwards all texture/buffer registrations into the given resource set.
    /// Called once after <see cref="Compile"/> when the graph was dirty.
    /// </summary>
    private void ApplyRegistrations(RenderGraphResourceSet resourceSet)
    {
        foreach (var kvp in _textureRegistrations)
        {
            resourceSet.AddTexture(kvp.Key, kvp.Value.BuildFunc, kvp.Value.DependsOnScreenSize);
        }
        foreach (var kvp in _bufferRegistrations)
        {
            resourceSet.AddBuffer(kvp.Key, kvp.Value.BuildFunc, kvp.Value.DependsOnScreenSize);
        }
    }

    private void SetupResourcesForPass(
        RenderContext context,
        ICommandBuffer cmdBuf,
        RenderGraphResourceSet resourceSet
    )
    {
        resourceSet.SetupSystemResources(context);
        foreach (var pass in _sortedPasses)
        {
            pass.OnSetupRenderParams(
                new RenderResources(
                    context,
                    cmdBuf,
                    pass.Pass,
                    pass.Framebuffer,
                    pass.Dependencies,
                    resourceSet.Textures,
                    resourceSet.Buffers
                )
            );
        }
    }
}
