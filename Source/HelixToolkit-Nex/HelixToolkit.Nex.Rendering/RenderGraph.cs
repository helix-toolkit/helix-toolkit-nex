namespace HelixToolkit.Nex.Rendering;

public enum ResourceType
{
    Texture,
    Buffer,
}

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
    private readonly Dictionary<string, BufferResource> _bufferResources = [];
    private readonly Dictionary<string, TextureResource> _textureResources = [];
    public Dictionary<string, BufferHandle> Buffers { get; } = [];
    public Dictionary<string, TextureHandle> Textures { get; } = [];

    public IReadOnlyList<GraphNode> SortedPasses => _sortedPasses;

    private int _screenWidth = 0;
    private int _screenHeight = 0;

    private readonly record struct BuildBufferFunction(
        Func<ResourceBuildParams, BufferResource> Func,
        bool DependsOnScreenSize
    );

    private readonly record struct BuildTextureFunction(
        Func<ResourceBuildParams, TextureResource> Func,
        bool DependsOnScreenSize
    );

    private readonly Dictionary<string, BuildBufferFunction?> _bufferBuilders = [];
    private readonly Dictionary<string, BuildTextureFunction?> _textureBuilders = [];

    public bool IsDirty { private set; get; } = true;

    public override string Name => nameof(RenderGraph);

    /// <summary>
    /// Adds a texture to the render graph with the specified name and build function.
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
        if (_textureBuilders.TryGetValue(name, out var func) && func != null)
        {
            throw new InvalidOperationException(
                $"A texture with the name '{name}' already exists in the render graph."
            );
        }
        _textureBuilders[name] = buildFunc is not null ? new(buildFunc, dependsOnScreenSize) : null;
        if (_textureResources.TryGetValue(name, out var texture))
        {
            texture.Dispose();
        }
        _textureResources[name] = TextureResource.Null;
        return this;
    }

    public RenderGraph AddFinalOutputTexture()
    {
        return AddTexture(SystemBufferNames.FinalOutputTexture, null);
    }

    /// <summary>
    /// Adds a buffer to the render graph with the specified name and build function.
    /// </summary>
    /// <remarks>If a buffer with the specified name already exists, it will be disposed and replaced with a
    /// new buffer resource.</remarks>
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
        if (_bufferBuilders.TryGetValue(name, out var func) && func != null)
        {
            throw new InvalidOperationException(
                $"A buffer with the name '{name}' already exists in the render graph."
            );
        }
        _bufferBuilders[name] = buildFunc is not null ? new(buildFunc, dependsOnScreenSize) : null;
        if (_bufferResources.TryGetValue(name, out var buffer))
        {
            buffer.Dispose();
        }
        _bufferResources[name] = BufferResource.Null;
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
    /// <remarks>After removing the specified pass, the method clears the resource producers and sorted
    /// passes, marking the render graph as dirty. This indicates that the graph needs to be re-evaluated before the
    /// next rendering operation.</remarks>
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
    /// <remarks>This method processes the render passes to determine the order in which they should be
    /// executed  based on their dependencies. It identifies producers for each resource, constructs a dependency
    /// graph, and uses Kahn's algorithm to sort the passes topologically. If a circular dependency is  detected, an
    /// <see cref="InvalidOperationException"/> is thrown.</remarks>
    /// <exception cref="InvalidOperationException">Thrown if a circular dependency is detected in the render graph, indicating that the passes  cannot be sorted
    /// topologically.</exception>
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
        // Track added edges to avoid counting duplicate edges from the same producer
        // when a consumer lists the same resource-name more than once in its inputs.
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
        if (IsDirty)
        {
            _screenWidth = context.WindowSize.Width;
            _screenHeight = context.WindowSize.Height;
            Compile();
            CreateAllResources(context);
        }
        if (context.WindowSize.Width != _screenWidth || context.WindowSize.Height != _screenHeight)
        {
            _screenWidth = context.WindowSize.Width;
            _screenHeight = context.WindowSize.Height;
            OnScreenSizeChanged(context);
        }
        SetupResourcesForPass(context, cmdBuf);
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
                    Textures,
                    Buffers
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
        DisposeResources();
        return ResultCode.Ok;
    }

    private void CreateAllResources(RenderContext context)
    {
        _logger.LogInformation(
            "Creating all render graph resources for screen size {WIDTH}x{HEIGHT}.",
            context.WindowSize.Width,
            context.WindowSize.Height
        );
        DisposeResources();
        var resourceParams = new ResourceBuildParams(context);
        Buffers.Clear();
        Textures.Clear();
        foreach (var builder in _bufferBuilders)
        {
            if (builder.Value == null)
            {
                Buffers[builder.Key] = BufferHandle.Null;
                continue;
            }
            var buf = builder.Value.Value.Func(resourceParams);
            _bufferResources[builder.Key] = buf;
            Buffers[builder.Key] = buf;
        }
        foreach (var builder in _textureBuilders)
        {
            if (builder.Value == null)
            {
                Textures[builder.Key] = TextureHandle.Null;
                continue;
            }
            var buf = builder.Value.Value.Func(resourceParams);
            _textureResources[builder.Key] = buf;
            Textures[builder.Key] = buf;
        }
    }

    private void OnScreenSizeChanged(RenderContext context)
    {
        _logger.LogInformation(
            "Screen size changed to {WIDTH}x{HEIGHT}. Recreating dependent resources.",
            context.WindowSize.Width,
            context.WindowSize.Height
        );
        var resourceParams = new ResourceBuildParams(context);
        foreach (var builder in _bufferBuilders)
        {
            if (builder.Value == null)
            {
                _bufferResources[builder.Key]?.Dispose();
                Buffers[builder.Key] = BufferHandle.Null;
                continue;
            }
            if (builder.Value.Value.DependsOnScreenSize)
            {
                _bufferResources[builder.Key]?.Dispose();
                var buf = builder.Value.Value.Func(resourceParams);
                _bufferResources[builder.Key] = buf;
                Buffers[builder.Key] = buf;
            }
        }
        foreach (var builder in _textureBuilders)
        {
            if (builder.Value == null)
            {
                _bufferResources[builder.Key]?.Dispose();
                Textures[builder.Key] = TextureHandle.Null;
                continue;
            }
            if (builder.Value.Value.DependsOnScreenSize)
            {
                _textureResources[builder.Key]?.Dispose();
                var buf = builder.Value.Value.Func(resourceParams);
                _textureResources[builder.Key] = buf;
                Textures[builder.Key] = buf;
            }
        }
    }

    private void SetupResourcesForPass(RenderContext context, ICommandBuffer cmdBuf)
    {
        // Inject well-known system resources that have no GPU-side builder —
        // only for entries that this graph actually declared, so sub-graphs that
        // don't need a resource won't crash with a KeyNotFoundException.
        if (Textures.ContainsKey(SystemBufferNames.FinalOutputTexture))
        {
            Textures[SystemBufferNames.FinalOutputTexture] = context.FinalOutputTexture;
        }
        if (Buffers.ContainsKey(SystemBufferNames.BufferMeshDrawOpaque))
        {
            Buffers[SystemBufferNames.BufferMeshDrawOpaque] =
                context.Data?.MeshDrawsOpaque.Buffer ?? BufferResource.Null;
        }
        if (Buffers.ContainsKey(SystemBufferNames.BufferMeshDrawTransparent))
        {
            Buffers[SystemBufferNames.BufferMeshDrawTransparent] =
                context.Data?.MeshDrawsTransparent.Buffer ?? BufferResource.Null;
        }
        if (Buffers.ContainsKey(SystemBufferNames.BufferDirectionalLight))
        {
            Buffers[SystemBufferNames.BufferDirectionalLight] =
                context.Data?.DirectionalLights.Buffer ?? BufferResource.Null;
        }
        if (Buffers.ContainsKey(SystemBufferNames.BufferLights))
        {
            Buffers[SystemBufferNames.BufferLights] =
                context.Data?.Lights.Buffer ?? BufferResource.Null;
        }
        foreach (var pass in _sortedPasses)
        {
            pass.OnSetupRenderParams(
                new RenderResources(
                    context,
                    cmdBuf,
                    pass.Pass,
                    pass.Framebuffer,
                    pass.Dependencies,
                    Textures,
                    Buffers
                )
            );
        }
    }

    private void DisposeResources()
    {
        foreach (var handle in _bufferResources.Values)
        {
            handle.Dispose();
        }
        foreach (var handle in _textureResources.Values)
        {
            handle.Dispose();
        }
    }
}
