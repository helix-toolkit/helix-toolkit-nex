namespace HelixToolkit.Nex.Rendering;

public enum ResourceType
{
    Texture,
    Buffer,
}

public readonly record struct RenderResource(string Name, ResourceType Type);

public sealed record ResourceBuildParams(IContext Context, int ScreenWidth, int ScreenHeight);

public sealed class RenderGraph(IServiceProvider serviceProvider) : Initializable
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(RenderGraph));
    private static readonly ILogger _logger = LogManager.Create<RenderGraph>();

    private sealed class GraphNode(
        string passName,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs,
        Action<
            RenderPass,
            Framebuffer,
            Dependencies,
            RenderContext,
            IReadOnlyDictionary<string, BufferHandle>,
            IReadOnlyDictionary<string, TextureHandle>
        > onSetup
    )
    {
        public readonly RenderPass Pass = new();
        public readonly Framebuffer Framebuffer = new();
        public readonly Dependencies Dependencies = new();
        public readonly string PassName = passName;
        public readonly IList<RenderResource> Inputs = inputs;
        public readonly IList<RenderResource> Outputs = outputs;
        public readonly Action<
            RenderPass,
            Framebuffer,
            Dependencies,
            RenderContext,
            IReadOnlyDictionary<string, BufferHandle>,
            IReadOnlyDictionary<string, TextureHandle>
        > OnSetup = onSetup;
    }

    private readonly IServiceProvider _services = serviceProvider;
    private readonly Dictionary<string, GraphNode> _passes = [];
    private readonly Dictionary<string, List<GraphNode>> _resourceProducers = [];
    private readonly List<GraphNode> _sortedPasses = [];
    private readonly Dictionary<string, BufferResource> _bufferResources = [];
    private readonly Dictionary<string, TextureResource> _textureResources = [];
    public Dictionary<string, BufferHandle> Buffers { private set; get; } = [];
    public Dictionary<string, TextureHandle> Textures { private set; get; } = [];
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
        Action<
            RenderPass,
            Framebuffer,
            Dependencies,
            RenderContext,
            IReadOnlyDictionary<string, BufferHandle>,
            IReadOnlyDictionary<string, TextureHandle>
        > onSetup
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

        // Build edges: if pass B depends on output from pass A, then A -> B
        foreach (var pass in _passes.Values)
        {
            foreach (var input in pass.Inputs)
            {
                if (_resourceProducers.TryGetValue(input.Name, out var producers))
                {
                    // AddPass edge from producer to consumer
                    foreach (var producer in producers)
                        adjacencyList[producer].Add(pass);
                    inDegree[pass]++;
                }
                // If no producer found, assume it's an external resource (e.g., swapchain image)
            }
        }

        // 3. Perform Kahn's algorithm for topological sort
        var queue = new Queue<GraphNode>();

        // Start with nodes that have no dependencies
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

            // Process all dependent passes
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
            CreateAllResources(context.Context);
        }
        SetupResourcesForPass(context);
        foreach (var pass in _sortedPasses)
        {
            if (!nodes.TryGetValue(pass.PassName, out var node))
            {
                _logger.LogTrace(
                    "No render node found for pass '{PASS}'. Skipping this pass.",
                    pass.PassName
                );
                continue; // No Node for this pass, skip it
            }
            if (!node.Enabled)
            {
                continue; // Node is disabled, skip it
            }
            node.Render(context, cmdBuf, pass.Pass, pass.Framebuffer, pass.Dependencies);
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

    private void CreateAllResources(IContext context)
    {
        DisposeResources();
        var resourceParams = new ResourceBuildParams(context, _screenWidth, _screenHeight);
        foreach (var builder in _bufferBuilders)
        {
            if (builder.Value == null)
            {
                _bufferResources[builder.Key] = BufferResource.Null;
                continue;
            }
            var resource = builder.Value(resourceParams);
            _bufferResources[builder.Key] = resource;
        }
        foreach (var builder in _textureBuilders)
        {
            if (builder.Value == null)
            {
                _textureResources[builder.Key] = TextureResource.Null;
                continue;
            }
            var resource = builder.Value(resourceParams);
            _textureResources[builder.Key] = resource;
        }

        Buffers = _bufferResources.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Handle);
        Textures = _textureResources.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Handle);
    }

    private void SetupResourcesForPass(RenderContext context)
    {
        Textures[SystemBufferNames.FinalOutputTexture] = context.FinalOutputTexture;
        Buffers[SystemBufferNames.ForwardPlusConstants] = context.FPConstantsBuffer;
        Buffers[SystemBufferNames.BufferMeshDrawOpaque] =
            context.Data?.MeshDrawsOpaque.Buffer ?? BufferHandle.Null;
        Buffers[SystemBufferNames.BufferMeshDrawTransparent] =
            context.Data?.MeshDrawsTransparent.Buffer ?? BufferHandle.Null;
        {
            Debug.Assert(Textures[SystemBufferNames.FinalOutputTexture].Valid);
            Debug.Assert(Buffers[SystemBufferNames.ForwardPlusConstants].Valid);
            Debug.Assert(
                Buffers[SystemBufferNames.BufferMeshDrawOpaque].Valid
                    || Buffers[SystemBufferNames.BufferMeshDrawTransparent].Valid
            );
        }
        foreach (var pass in _sortedPasses)
        {
            pass.OnSetup(
                pass.Pass,
                pass.Framebuffer,
                pass.Dependencies,
                context,
                Buffers,
                Textures
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
