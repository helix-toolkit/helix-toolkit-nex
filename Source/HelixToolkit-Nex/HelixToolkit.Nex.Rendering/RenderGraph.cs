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

    private sealed class GraphNode(
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

    private int _screenWidth = 0;
    private int _screenHeight = 0;

    private readonly Dictionary<
        string,
        Func<ResourceBuildParams, BufferResource>?
    > _bufferBuilders = [];
    private readonly Dictionary<
        string,
        Func<ResourceBuildParams, TextureResource>?
    > _textureBuilders = [];

    public bool IsDirty { private set; get; } = true;

    public override string Name => nameof(RenderGraph);

    public RenderGraph AddTexture(
        string name,
        Func<ResourceBuildParams, TextureResource>? buildFunc
    )
    {
        if (_textureBuilders.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"A texture with the name '{name}' already exists in the render graph."
            );
        }
        _textureBuilders[name] = buildFunc;
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

    public RenderGraph AddBuffer(string name, Func<ResourceBuildParams, BufferResource>? buildFunc)
    {
        if (_bufferBuilders.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"A buffer with the name '{name}' already exists in the render graph."
            );
        }
        _bufferBuilders[name] = buildFunc;
        if (_bufferResources.TryGetValue(name, out var buffer))
        {
            buffer.Dispose();
        }
        _bufferResources[name] = BufferResource.Null;
        return this;
    }

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

    public RenderGraph RemovePass(string passName)
    {
        _passes.Remove(passName);
        _resourceProducers.Clear();
        _sortedPasses.Clear();
        IsDirty = true;
        return this;
    }

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
            Compile();
        }
        if (context.WindowSize.Width != _screenWidth || context.WindowSize.Height != _screenHeight)
        {
            _screenWidth = context.WindowSize.Width;
            _screenHeight = context.WindowSize.Height;
            CreateAllResources(context);
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
            var buf = builder.Value(resourceParams);
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
            var buf = builder.Value(resourceParams);
            _textureResources[builder.Key] = buf;
            Textures[builder.Key] = buf;
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
