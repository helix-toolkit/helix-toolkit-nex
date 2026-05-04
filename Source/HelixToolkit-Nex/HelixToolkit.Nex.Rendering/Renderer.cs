namespace HelixToolkit.Nex.Rendering;

public class Renderer(IServiceProvider serviceProvider) : Initializable
{
    private static readonly ILogger _logger = LogManager.Create<Renderer>();
    private readonly Dictionary<string, RenderNode> _renderers = [];
    private readonly FastList<RenderStage> _renderSequence = [
        RenderStage.Prepare,
        RenderStage.SubmitFlag,
        RenderStage.Opaque,
        RenderStage.Antialising,
        RenderStage.Particle,
        RenderStage.Transparent,
        RenderStage.SubmitFlag,
        RenderStage.PostProcess,
        RenderStage.Bloom,
        RenderStage.Billboard,
        RenderStage.ToneMap,
        RenderStage.Overlay,
        RenderStage.Output,
        ];

    public IServiceProvider Services { get; } = serviceProvider;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public IResourceManager ResourceManager { get; } =
        serviceProvider.GetRequiredService<IResourceManager>();

    public IContext Context => ResourceManager.Context;
    public IShaderRepository ShaderRepository => ResourceManager.ShaderRepository;

    public override string Name => nameof(Renderer);

    public IEnumerable<RenderNode> RenderNodes => _renderers.Values;

    public IReadOnlyDictionary<string, RenderNode> RenderNodeMap => _renderers;

    public uint NumFrameInFlight => Math.Max(1, Math.Min(Context.GetNumSwapchainImages(), RenderSettings.MaxFrameInFlight));

    public bool AddNode(RenderNode renderer)
    {
        return AddNode(renderer.Name, renderer);
    }

    public bool AddNode(string name, RenderNode renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (_renderers.ContainsKey(renderer.Name))
        {
            _logger.LogWarning(
                "A renderer with the name '{RendererName}' already exists. Skipping addition.",
                renderer.Name
            );
            return false;
        }
        _renderers[renderer.Name] = renderer;
        if (!IsInitialized)
        {
            return true;
        }
        if (renderer.Setup(this))
        {
            return true;
        }
        _logger.LogError(
            "Failed to set up renderer '{RendererName}'. Renderer was not added.",
            renderer.Name
        );
        return false;
    }

    public void RemoveNode(RenderNode renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (_renderers.TryGetValue(renderer.Name, out var node))
        {
            node.TearDown();
        }
        else
        {
            _logger.LogWarning(
                "No renderers found with the name '{RendererName}'. No action taken.",
                renderer.Name
            );
        }
        _renderers.Remove(renderer.Name);
    }

    public bool TryGetRenderNode(string name, out RenderNode? node)
    {
        return _renderers.TryGetValue(name, out node);
    }

    public void Clear()
    {
        foreach (var kvp in _renderers)
        {
            kvp.Value.TearDown();
        }
        _renderers.Clear();
    }

    public ICommandBuffer Render(RenderContext context, RenderGraph graph)
    {
        var cmdBuf = context.Context.AcquireCommandBuffer();
        if (context.WindowSize.Width <= 1 || context.WindowSize.Height <= 1)
        {
            _logger.LogDebug(
                "Window size is too small for rendering (Width: {Width}, Height: {Height}). Skipping render.",
                context.WindowSize.Width,
                context.WindowSize.Height
            );
            return cmdBuf;
        }
        context.BeginFrame();
        foreach (var stage in _renderSequence)
        {
            if (stage == RenderStage.SubmitFlag)
            {
                context.Context.Submit(cmdBuf);
                cmdBuf = context.Context.AcquireCommandBuffer();
                continue;
            }
            graph.Execute(context, cmdBuf, _renderers, stage);
        }
        context.EndFrame();
        return cmdBuf;
    }

    protected override ResultCode OnInitializing()
    {
        foreach (var kvp in _renderers)
        {
            if (!kvp.Value.Setup(this))
            {
                _logger.LogError(
                    "Failed to set up renderer '{RendererName}' during initialization.",
                    kvp.Key
                );
                return ResultCode.RuntimeError;
            }
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Clear();
        return ResultCode.Ok;
    }
}
