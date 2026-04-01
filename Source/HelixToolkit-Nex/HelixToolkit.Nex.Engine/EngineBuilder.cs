using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Fluent builder for creating and configuring an <see cref="Engine"/> instance.
/// <para>
/// Eliminates the boilerplate of manually wiring services, render nodes, post-effects,
/// and lifecycle calls. Common presets are available via <see cref="UseForwardPlusDefaults"/>
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
///         effects.AddEffect(new ToneMapping());
///     })
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
    private PostEffectsNode _postEffectsNode = new();
    private bool _addRenderToFinal;
    private bool _createPBRMaterials;
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
    public EngineBuilder AddRenderToFinal()
    {
        _addRenderToFinal = true;
        return this;
    }

    /// <summary>
    /// Calls <see cref="IMaterialManager.CreatePBRMaterialsFromRegistry"/> during
    /// <see cref="Build"/> after the <see cref="IResourceManager"/> is created.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder CreatePBRMaterials()
    {
        _createPBRMaterials = true;
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
    ///     .AddNode(new ForwardPlusLightCullingNode())
    ///     .AddNode(new ForwardPlusOpaqueNode())
    ///     .AddNode(new PostEffectsNode())
    ///     .AddRenderToFinal()
    ///     .CreatePBRMaterials();
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="renderToSwapchain">Whether engine renders onto swapchain. Set it to false if engine should render onto an external texture.</param>
    /// <returns>This builder for method chaining.</returns>
    public EngineBuilder UseForwardPlusDefaults(bool renderToSwapchain = true)
    {
        AddNode(new PrepareNode());
        AddNode(new DepthPassNode());
        AddNode(new FrustumCullNode());
        AddNode(new ForwardPlusLightCullingNode());
        AddNode(new ForwardPlusOpaqueNode());
        _addRenderToFinal = renderToSwapchain;
        _createPBRMaterials = true;
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

        if (_createPBRMaterials)
        {
            engine.ResourceManager.Materials.CreatePBRMaterialsFromRegistry();
        }
        _onResourceManagerReady?.Invoke(engine.ResourceManager);

        // --- Add render nodes ---
        foreach (var node in _nodes)
        {
            engine.AddNode(node);
        }
        engine.AddNode(_postEffectsNode);
        _postEffectsNode = new();
        if (_addRenderToFinal)
        {
            engine.AddNode(new RenderToFinalNode(_context.GetSwapchainFormat()));
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
