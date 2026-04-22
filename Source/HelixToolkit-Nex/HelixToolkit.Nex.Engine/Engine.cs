namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Top-level coordinator for the 3D rendering engine.
/// <para>
/// Owns and manages the shared rendering infrastructure:
/// <list type="number">
/// <item><see cref="IContext"/> — GPU abstraction (created externally, resolved via DI)</item>
/// <item><see cref="IResourceManager"/> — geometry, material, and shader pools</item>
/// <item><see cref="Renderer"/> — collection of <see cref="RenderNode"/>s</item>
/// <item><see cref="RenderGraph"/> — DAG of render passes with automatic dependency resolution</item>
/// </list>
/// </para>
/// <para>
/// <b>Per-viewport state</b> is held in <see cref="RenderContext"/> instances, created via
/// <see cref="CreateRenderContext"/>. Each render context owns its own resource set, window
/// size, camera, and statistics — enabling multi-viewport rendering with a single engine.
/// </para>
/// <para>
/// <b>Scene data</b> is decoupled from the engine. A <see cref="WorldDataProvider"/> (or any
/// <see cref="IRenderDataProvider"/>) is passed to <see cref="Render"/> or
/// <see cref="RenderOffscreen"/> each frame.
/// </para>
/// <para>
/// <b>Usage pattern — single viewport:</b>
/// <code>
/// var engine = new Engine(config);
/// engine.AddNode(new PrepareNode());
/// engine.AddNode(new DepthPassNode());
/// engine.AddNode(new ForwardPlusOpaqueNode());
/// engine.Initialize();
///
/// var viewport = engine.CreateRenderContext();
/// viewport.Initialize();
/// var worldData = new WorldDataProvider(config.Services);
/// worldData.Initialize();
///
/// // In game loop:
/// viewport.WindowSize = new Size(width, height);
/// viewport.CameraParams = camera.ToCameraParams(aspectRatio);
/// engine.Render(viewport, worldData);
/// </code>
/// </para>
/// <para>
/// <b>Usage pattern — multi-viewport:</b>
/// <code>
/// var mainViewport = engine.CreateRenderContext();
/// var previewViewport = engine.CreateRenderContext();
/// mainViewport.Initialize();
/// previewViewport.Initialize();
///
/// // Render main scene to swapchain
/// mainViewport.CameraParams = mainCamera.ToCameraParams(mainAspect);
/// engine.Render(mainViewport, worldData);
///
/// // Render preview at different resolution
/// previewViewport.WindowSize = new Size(256, 256);
/// previewViewport.CameraParams = previewCamera.ToCameraParams(1f);
/// var cmdBuf = engine.RenderOffscreen(previewViewport, worldData);
/// context.Submit(cmdBuf, TextureHandle.Null);
/// </code>
/// </para>
/// </summary>
public class Engine : Initializable
{
    private static readonly ILogger _logger = LogManager.Create<Engine>();
    private readonly IInitializable[] _initializables;

    /// <summary>
    /// Gets the GPU graphics context.
    /// </summary>
    public IContext Context { get; }

    /// <summary>
    /// Gets the engine configuration.
    /// </summary>
    public EngineConfig Config { get; }

    /// <summary>
    /// Gets the shared resource manager for geometries, materials, and shaders.
    /// </summary>
    public IResourceManager ResourceManager { get; }

    /// <summary>
    /// Gets the renderer that owns all <see cref="RenderNode"/>s.
    /// </summary>
    public Renderer Renderer { get; }

    /// <summary>
    /// Gets the render graph that defines the pass execution order.
    /// </summary>
    public RenderGraph RenderGraph { get; }

    public override string Name => nameof(Engine);

    /// <summary>
    /// Creates a new engine instance.
    /// <para>
    /// The <paramref name="config"/> must provide an <see cref="IServiceProvider"/> with at least
    /// an <see cref="IContext"/> registration. An <see cref="IResourceManager"/> registration is
    /// optional — if absent, a default <see cref="ResourceManager"/> is created automatically.
    /// </para>
    /// </summary>
    /// <param name="config">Engine configuration containing the DI service provider.</param>
    public Engine(EngineConfig config)
    {
        Config = config;
        Context = Config.Services.GetRequiredService<IContext>();
        ResourceManager =
            Config.Services.GetService<IResourceManager>() ?? new ResourceManager(Config.Services);

        Renderer = new Renderer(Config.Services);
        RenderGraph = new RenderGraph(Config.Services);

        // Initialization order matters: Renderer → RenderGraph.
        // Teardown runs in reverse order.
        _initializables = [Renderer, RenderGraph, ResourceManager];
    }

    /// <summary>
    /// Creates a new <see cref="RenderContext"/> configured for this engine.
    /// <para>
    /// Each render context holds its own per-viewport state: window size, camera parameters,
    /// render statistics, and a <see cref="RenderGraphResourceSet"/> that manages resolution-dependent
    /// GPU resources (depth buffers, color targets, etc.).
    /// </para>
    /// <para>
    /// The caller owns the returned context and must call <see cref="Initializable.Initialize"/>
    /// before use and <see cref="Initializable.Teardown"/> (or <c>Dispose</c>) when done.
    /// </para>
    /// </summary>
    /// <returns>A new, uninitialized render context.</returns>
    public RenderContext CreateRenderContext()
    {
        var renderContext = new RenderContext(Config.Services);
        return renderContext;
    }

    /// <summary>
    /// Creates a new <see cref="WorldDataProvider"/> configured for this engine.
    /// <para>
    /// Each world data provider owns its own <see cref="World"/> and manages the ECS-to-GPU
    /// data pipeline (mesh draws, lights, materials). Multiple providers can coexist,
    /// enabling multi-scene rendering with a single engine.
    /// </para>
    /// <para>
    /// The caller owns the returned provider and must call <see cref="WorldDataProvider.Initialize"/>
    /// before use and <see cref="WorldDataProvider.Dispose"/> when done.
    /// </para>
    /// </summary>
    /// <returns>A new, uninitialized world data provider.</returns>
    public WorldDataProvider CreateWorldDataProvider()
    {
        return new WorldDataProvider(Config.Services);
    }

    /// <summary>
    /// Adds a <see cref="RenderNode"/> to the renderer and registers its passes in the render graph.
    /// <para>
    /// Must be called <b>before</b> <see cref="Initializable.Initialize"/>. Nodes added after
    /// initialization will be set up immediately but will also mark the render graph dirty,
    /// causing a recompile on the next <see cref="Render"/> call.
    /// </para>
    /// </summary>
    /// <param name="name">The render node name if different from <see cref="RenderNode.Name"/></param>
    /// <param name="node">The render node to add.</param>
    /// <returns><c>true</c> if the node was added successfully.</returns>
    public bool AddNode(string name, RenderNode node)
    {
        if (!Renderer.AddNode(name, node))
        {
            return false;
        }
        node.AddToGraph(RenderGraph);
        return true;
    }

    /// <summary>
    /// Adds a <see cref="RenderNode"/> to the renderer and registers its passes in the render graph.
    /// <para>
    /// Must be called <b>before</b> <see cref="Initializable.Initialize"/>. Nodes added after
    /// initialization will be set up immediately but will also mark the render graph dirty,
    /// causing a recompile on the next <see cref="Render"/> call.
    /// </para>
    /// </summary>
    /// <param name="node">The render node to add.</param>
    /// <returns><c>true</c> if the node was added successfully.</returns>
    public bool AddNode(RenderNode node)
    {
        return AddNode(node.Name, node);
    }

    /// <summary>
    /// Removes a <see cref="RenderNode"/> from the renderer and marks the render graph dirty.
    /// </summary>
    /// <param name="node">The render node to remove.</param>
    public void RemoveNode(RenderNode node)
    {
        Renderer.RemoveNode(node);
        RenderGraph.RemovePass(node.Name);
    }

    /// <summary>
    /// Attempts to retrieve a render node by its name.
    /// </summary>
    /// <param name="name">The name of the render node to retrieve. Cannot be <see langword="null"/> or empty.</param>
    /// <param name="node">When this method returns, contains the <see cref="RenderNode"/> associated with the specified name, if the
    /// render node is found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a render node with the specified name is found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRenderNode(string name, out RenderNode? node)
    {
        return Renderer.TryGetRenderNode(name, out node);
    }

    /// <summary>
    /// Attempts to retrieve a render node by its name and cast it to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected concrete type of the render node.</typeparam>
    /// <param name="name">The name of the render node to retrieve.</param>
    /// <param name="node">When this method returns, contains the typed render node if found and the type matches; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a render node with the specified name and type is found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRenderNode<T>(string name, out T? node)
        where T : RenderNode
    {
        if (Renderer.TryGetRenderNode(name, out var rawNode) && rawNode is T typed)
        {
            node = typed;
            return true;
        }
        node = null;
        return false;
    }

    /// <summary>
    /// Returns the first render node of type <typeparamref name="T"/>, or <see langword="null"/>
    /// if no matching node is registered.
    /// <para>
    /// This is a convenience overload for ImGui debug panels and runtime tweaking where the
    /// caller knows only the node type, not its string name. The result can be cached once
    /// and reused across frames since nodes are stable after initialization.
    /// </para>
    /// <para>
    /// <b>Example — ImGui slider for light culling:</b>
    /// <code>
    /// // Cache once (e.g., in a field):
    /// _opaqueNode ??= engine.GetRenderNode&lt;ForwardPlusOpaqueNode&gt;();
    ///
    /// // Every frame in ImGui:
    /// if (_opaqueNode is not null)
    /// {
    ///     var useLc = _opaqueNode.UseLightCulling;
    ///     ImGui.Checkbox("Light Culling", ref useLc);
    ///     _opaqueNode.UseLightCulling = useLc;
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="RenderNode"/> type to find.</typeparam>
    /// <returns>The first matching node, or <see langword="null"/>.</returns>
    public T? GetRenderNode<T>()
        where T : RenderNode
    {
        foreach (var node in Renderer.RenderNodes)
        {
            if (node is T typed)
            {
                return typed;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the first post-effect of type <typeparamref name="T"/> from the
    /// <see cref="PostEffectsNode"/>, or <see langword="null"/> if the engine has no
    /// <see cref="PostEffectsNode"/> or no matching effect is registered.
    /// <para>
    /// This is a convenience shortcut for ImGui debug panels. Cache the result once
    /// and reuse it across frames — effects are stable after initialization.
    /// </para>
    /// <para>
    /// <b>Example — ImGui controls for bloom:</b>
    /// <code>
    /// _bloom ??= engine.GetPostEffect&lt;Bloom&gt;();
    /// if (_bloom is not null)
    /// {
    ///     var threshold = _bloom.Threshold;
    ///     ImGui.SliderFloat("Bloom Threshold", ref threshold, 0f, 2f);
    ///     _bloom.Threshold = threshold;
    ///
    ///     var intensity = _bloom.Intensity;
    ///     ImGui.SliderFloat("Bloom Intensity", ref intensity, 0f, 10f);
    ///     _bloom.Intensity = intensity;
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="PostEffect"/> type to find.</typeparam>
    /// <returns>The first matching effect, or <see langword="null"/>.</returns>
    public T? GetPostEffect<T>()
        where T : PostEffect
    {
        var postEffectsNode = GetRenderNode<PostEffectsNode>();
        return postEffectsNode?.GetEffect<T>();
    }

    /// <summary>
    /// Executes a full render frame: acquires the swapchain texture, executes the render
    /// graph, and presents.
    /// </summary>
    /// <param name="renderContext">
    /// The per-viewport render context (camera, window size, resource set). Created via
    /// <see cref="CreateRenderContext"/>. The engine does not own this object.
    /// </param>
    /// <param name="dataProvider">
    /// The data provider that feeds scene data (meshes, lights, materials) for this frame.
    /// Typically a <see cref="WorldDataProvider"/> instance. The engine does not own or
    /// dispose this object.
    /// </param>
    public void Render(RenderContext renderContext, IRenderDataProvider dataProvider)
    {
        renderContext.Data = dataProvider;
        renderContext.FinalOutputTexture = Context.GetCurrentSwapchainTexture();
        Renderer.Render(renderContext, RenderGraph);
    }

    /// <summary>
    /// Executes the render graph into an offscreen target without presenting.
    /// Returns the <see cref="ICommandBuffer"/> so the caller can record additional
    /// work (e.g., an ImGui composite pass) before submitting.
    /// </summary>
    /// <param name="renderContext">
    /// The per-viewport render context (camera, window size, resource set). Created via
    /// <see cref="CreateRenderContext"/>. The engine does not own this object.
    /// </param>
    /// <param name="dataProvider">
    /// The data provider that feeds scene data (meshes, lights, materials) for this frame.
    /// Typically a <see cref="WorldDataProvider"/> instance. The engine does not own or
    /// dispose this object.
    /// </param>
    /// <returns>The command buffer with recorded render graph commands.</returns>
    public ICommandBuffer RenderOffscreen(
        RenderContext renderContext,
        IRenderDataProvider dataProvider
    )
    {
        renderContext.Data = dataProvider;
        return Renderer.RenderOffscreen(renderContext, RenderGraph);
    }

    protected override ResultCode OnInitializing()
    {
        for (var i = 0; i < _initializables.Length; ++i)
        {
            ResultCode ret = _initializables[i].Initialize();
            if (ret != ResultCode.Ok)
            {
                _logger.LogError(
                    "Failed to initialize '{Name}'. Result: {Result}",
                    _initializables[i].Name,
                    ret
                );
                return ret;
            }
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        for (var i = _initializables.Length - 1; i >= 0; --i)
        {
            ResultCode ret = _initializables[i].Teardown();
            if (ret != ResultCode.Ok)
            {
                _logger.LogError(
                    "Failed to tear down '{Name}'. Result: {Result}",
                    _initializables[i].Name,
                    ret
                );
                return ret;
            }
        }
        return ResultCode.Ok;
    }
}
