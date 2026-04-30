using HelixToolkit.Nex.Rendering.ComputeNodes;

namespace HelixToolkit.Nex.Engine;

public enum EngineInteropTarget
{
    None,
    WPF,
    WinUI,
};

/// <summary>
/// Fluent builder for creating and configuring an <see cref="Engine"/> instance.
/// <para>
/// Eliminates the boilerplate of manually wiring services, render nodes, post-effects,
/// and lifecycle calls. Common presets are available via <see cref="WithDefaultNodes"/>
/// and <see cref="UseDepthPrepassDefaults"/>.
/// </para>
/// <para>
/// The builder creates only the shared rendering infrastructure. Per-viewport state
/// is created via <see cref="Engine.CreateRenderContext"/>, and scene data
/// (<see cref="WorldDataProvider"/>) is passed to <see cref="Engine.Render"/> or
/// <see cref="Engine.RenderOffscreen"/> each frame.
/// </para>
/// <para>
/// <b>Example — quick start with defaults:</b>
/// <code>
/// using var engine = EngineBuilder.Create(context)
///     .UseForwardPlusDefaults()
///     .Build();
///
/// // Create per-viewport state and scene data
/// var viewport = engine.CreateRenderContext();
/// viewport.Initialize();
/// var worldData = engine.CreateWorldDataProvider();
/// worldData.Initialize();
///
/// // In game loop:
/// viewport.WindowSize = new Size(width, height);
/// viewport.CameraParams = camera.ToCameraParams(aspectRatio);
/// engine.Render(viewport, worldData);
/// </code>
/// </para>
/// <para>
/// <b>Example — custom pipeline:</b>
/// <code>
/// using var engine = EngineBuilder.Create(context)
///     .AddNode(new PrepareNode())
///     .AddNode(new DepthPassNode())
///     .AddNode(new FrustumCullNode())
///     .AddNode(new ForwardPlusOpaqueNode())
///     .AddNode(new ForwardPlusLightCullingNode())
///     .WithPostEffects(effects => {
///         effects.AddEffect(new Smaa());
///         effects.AddEffect(new Bloom());
///     })
///     .AddNode(new ToneMappingNode())
///     .AddNode(new RenderToFinalNode(context.GetSwapchainFormat()))
///     .Build();
/// </code>
/// </para>
/// </summary>
public sealed class EngineBuilder
{
    private readonly IContext _context;
    private readonly List<RenderNode> _nodes = [];
    private readonly List<Action<IServiceCollection>> _serviceConfigurators = [];
    private readonly List<Action<IReadOnlyList<RenderNode>>> _nodeConfigurators = [];
    private PostEffectsNode _postEffectsNode = new();
    private bool _addRenderToFinal;
    private Format _finalTextureFormat = Format.Invalid;
    private EngineInteropTarget _interopTarget = EngineInteropTarget.None;
    private Action<IResourceManager>? _onResourceManagerReady;

    private EngineBuilder(IContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Creates a new <see cref="EngineBuilder"/> with the given GPU context.
    /// /// </summary>
    /// <param name="context">The GPU graphics context (e.g., Vulkan backend).</param>
    /// <returns>A new builder instance.</returns>
    public static EngineBuilder Create(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new EngineBuilder(context);
    }

    /// <summary>
    /// Registers additional services into the DI container before the engine is created.
    /// </summary>
    /// <param name="configure">A callback that receives the <see cref="IServiceCollection"/>.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="RenderNode"/> to the engine's rendering pipeline.
    /// Nodes are added in the order they are registered; the render graph resolves
    /// execution order via resource dependencies and explicit <c>after</c> constraints.
    /// </summary>
    /// <param name="node">The render node to add.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder AddNode(RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return this;
    }

    /// <summary>
    /// Configures all previously added <see cref="RenderNode"/>s of type <typeparamref name="T"/>.
    /// <para>
    /// The callback is deferred until <see cref="Build"/> so it works with both manually
    /// added nodes and nodes added by presets such as <see cref="WithDefaultNodes"/>.
    /// If no node of the requested type exists, the callback is silently skipped.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// EngineBuilder.Create(context)
    ///     .WithDefaultNodes()
    ///     .ConfigureNode&lt;ForwardPlusOpaqueNode&gt;(n => n.UseLightCulling = false)
    ///     .Build();
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="RenderNode"/> type to configure.</typeparam>
    /// <param name="configure">A callback that receives each matching node instance.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder ConfigureNode<T>(Action<T> configure)
        where T : RenderNode
    {
        ArgumentNullException.ThrowIfNull(configure);
        _nodeConfigurators.Add(nodes =>
        {
            foreach (var node in nodes)
            {
                if (node is T typed)
                {
                    configure(typed);
                }
            }
        });
        return this;
    }

    /// <summary>
    /// Configures a <see cref="PostEffectsNode"/> with the specified effects.
    /// If called multiple times, effects are accumulated into a single node.
    /// </summary>
    /// <param name="configure">A callback that receives the <see cref="PostEffectsNode"/> to populate.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder WithPostEffects(Action<PostEffectsNode> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_postEffectsNode);
        return this;
    }

    /// <summary>
    /// Appends a <see cref="RenderToFinalNode"/> that copies the final color buffer
    /// to the swapchain. This is required when presenting to the screen (not needed
    /// for offscreen-only rendering, e.g., ImGui composite).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder RenderToCustomTarget(Format targetFormat)
    {
        _addRenderToFinal = true;
        _finalTextureFormat = targetFormat;
        return this;
    }

    /// <summary>
    /// Provides a callback to configure the <see cref="IResourceManager"/> after it
    /// is created but before the engine is initialized (e.g., register custom materials,
    /// load textures).
    /// </summary>
    /// <param name="configure">A callback that receives the <see cref="IResourceManager"/>.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder OnResourceManagerReady(Action<IResourceManager> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _onResourceManagerReady = configure;
        return this;
    }

    /// <summary>
    /// Configures a full Forward+ rendering pipeline with commonly used nodes and
    /// a standard set of post-effects (SMAA, Bloom, ToneMapping, ShowFPS).
    /// <para>
    /// This is equivalent to manually calling:
    /// <code>
    /// builder
    ///     .AddNode(new PrepareNode())
    ///     .AddNode(new DepthPassNode())
    ///     .AddNode(new FrustumCullNode())
    ///     .AddNode(new PointCullNode())
    ///     .AddNode(new ForwardPlusLightCullingNode())
    ///     .AddNode(new ForwardPlusOpaqueNode())
    ///     .AddNode(new PointRenderNode())
    ///     .AddNode(new PostEffectsNode())
    ///     .AddNode(new ForwardPlusTransparentNode())
    ///     .AddNode(new WBOITCompositeNode())
    ///     .AddNode(new ToneMappingNode())
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="renderToSwapchain">Whether engine renders onto swapchain. Set it to false if engine should render onto an external texture.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder WithDefaultNodes(bool renderToSwapchain = true)
    {
        AddNode(new PrepareNode());
        AddNode(new DepthPassNode());
        AddNode(new FrustumCullNode());
        AddNode(new PointCullNode());
        AddNode(new ForwardPlusLightCullingNode());
        AddNode(new ForwardPlusOpaqueNode());
        AddNode(new PointRenderNode());
        AddNode(new ForwardPlusTransparentNode());
        AddNode(new WBOITCompositeNode());
        AddNode(new ToneMappingNode());
        _addRenderToFinal = renderToSwapchain && _context.GetNumSwapchainImages() > 0;
        if (renderToSwapchain)
        {
            _finalTextureFormat = _context.GetSwapchainFormat();
        }
        return this;
    }

    /// <summary>
    /// Configures the engine builder to target Windows Presentation Foundation (WPF) interop.
    /// </summary>
    /// <remarks>Call this method when building an engine that will be used in a WPF application. This method
    /// enables WPF-specific interop features. This method can be chained with other configuration methods.</remarks>
    /// <returns>The current instance of <see cref="EngineBuilder"/> with WPF interop enabled.</returns>
    public EngineBuilder WithWpf()
    {
        WithInteropTarget(EngineInteropTarget.WPF);
        return this;
    }

    /// <summary>
    /// Build the engine with WinUI interop support. This configures the engine to use WinUI-compatible
    /// rendering and input handling.
    /// </summary>
    /// <returns>The current instance of <see cref="EngineBuilder"/> with WinUI interop enabled.</returns>
    public EngineBuilder WithWinUI()
    {
        WithInteropTarget(EngineInteropTarget.WinUI);
        return this;
    }

    /// <summary>
    /// Configures the engine to use the specified interop target for rendering operations.
    /// </summary>
    /// <remarks>If an interop target other than EngineInteropTarget.None is specified, additional rendering
    /// to the final output is enabled. This method supports fluent configuration by returning the same EngineBuilder
    /// instance.</remarks>
    /// <param name="target">The interop target to be used by the engine. Specify a value other than EngineInteropTarget.None to enable
    /// interop rendering.</param>
    /// <returns>The current instance of EngineBuilder with the updated interop target configuration.</returns>
    public EngineBuilder WithInteropTarget(EngineInteropTarget target)
    {
        _interopTarget = target;
        if (target != EngineInteropTarget.None)
        {
            _addRenderToFinal = true;
            _finalTextureFormat = target switch
            {
                EngineInteropTarget.WPF => Format.BGRA_UN8,
                EngineInteropTarget.WinUI => Format.RGBA_UN8,
                _ => Format.Invalid,
            };
        }
        return this;
    }

    /// <summary>
    /// Builds and initializes the <see cref="Engine"/>.
    /// <para>
    /// This method:
    /// <list type="number">
    /// <item>Creates the DI container with <see cref="IContext"/> and <see cref="IResourceManager"/></item>
    /// <item>Optionally creates PBR materials from the registry</item>
    /// <item>Adds all configured render nodes (including post-effects and RenderToFinal)</item>
    /// <item>Calls <see cref="Engine.Initialize"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// The returned engine has <b>no viewports or scene data</b>. Create per-viewport state
    /// via <see cref="Engine.CreateRenderContext"/> and pass an <see cref="IRenderDataProvider"/>
    /// to <see cref="Engine.Render"/> or <see cref="Engine.RenderOffscreen"/> each frame.
    /// </para>
    /// </summary>
    /// <returns>A fully initialized <see cref="Engine"/> instance ready for rendering.</returns>
    /// <exception cref="InvalidOperationException">Thrown if initialization fails.</exception>
    public Engine Build()
    {
        // --- Build DI container ---
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();
        foreach (var configurator in _serviceConfigurators)
        {
            configurator(services);
        }
        var serviceProvider = services.BuildServiceProvider();

        // --- Resource manager setup ---
        var config = new EngineConfig(serviceProvider);
        var engine = new Engine(config);

        engine.ResourceManager.PBRMaterialManager.CreatePBRMaterialsFromRegistry();
        engine.ResourceManager.PointMaterialManager.CreatePipelinesFromRegistry();
        _onResourceManagerReady?.Invoke(engine.ResourceManager);

        // --- Apply deferred node configurations ---
        foreach (var configurator in _nodeConfigurators)
        {
            configurator(_nodes);
        }

        // --- Add render nodes ---
        foreach (var node in _nodes)
        {
            engine.AddNode(node);
        }
        engine.AddNode(_postEffectsNode);
        _postEffectsNode = new();
        if (_addRenderToFinal)
        {
            if (_finalTextureFormat == Format.Invalid)
            {
                _finalTextureFormat = _context.GetSwapchainFormat();
            }
            engine.AddNode(new RenderToFinalNode(_finalTextureFormat));
        }

        // --- Initialize rendering infrastructure ---
        var result = engine.Initialize();
        if (result != ResultCode.Ok)
        {
            throw new InvalidOperationException(
                $"Engine initialization failed with result: {result}"
            );
        }

        return engine;
    }
}
