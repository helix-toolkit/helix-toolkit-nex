namespace HelixToolkit.Nex.Rendering;

public readonly record struct BuildBufferFunction(
    Func<ResourceBuildParams, BufferResource> Func,
    bool DependsOnScreenSize
);

public readonly record struct BuildTextureFunction(
    Func<ResourceBuildParams, TextureResource> Func,
    bool DependsOnScreenSize
);

public enum ResourceType
{
    Texture,
    Buffer,
}

/// <summary>
/// Holds GPU resources (textures and buffers) used by a <see cref="RenderGraph"/>.
/// <para>
/// The resource set is decoupled from the graph topology so that the same compiled
/// <see cref="RenderGraph"/> can be executed against different resource sets — for
/// example when rendering the same pass pipeline at multiple resolutions (main
/// viewport, shadow maps, reflection probes, etc.).
/// </para>
/// <para>
/// Lifecycle is owned by <see cref="RenderContext"/>; the <see cref="RenderGraph"/>
/// reads from the resource set during <see cref="RenderGraph.Execute"/> but never
/// creates or disposes it.
/// </para>
/// </summary>
public sealed class RenderGraphResourceSet : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<RenderGraphResourceSet>();

    private readonly Dictionary<string, BufferResource> _bufferResources = [];
    private readonly Dictionary<string, TextureResource> _textureResources = [];
    private readonly Dictionary<string, BuildBufferFunction?> _bufferBuilders = [];
    private readonly Dictionary<string, BuildTextureFunction?> _textureBuilders = [];

    /// <summary>
    /// Gets the live buffer handles keyed by resource name.
    /// </summary>
    public Dictionary<string, BufferHandle> Buffers { get; } = [];

    /// <summary>
    /// Gets the live texture handles keyed by resource name.
    /// </summary>
    public Dictionary<string, TextureHandle> Textures { get; } = [];

    private RenderGraph? _currentGraph;
    internal RenderGraph? CurrentGraph
    {
        set
        {
            if (_currentGraph != value)
            {
                _currentGraph = value;
                _logger.LogInformation(
                    "Current render graph set to '{GraphName}'.",
                    _currentGraph?.Name ?? "null"
                );
                LastUpdatedTimeStamp = 0;
            }
        }
        get => _currentGraph;
    }

    internal long LastUpdatedTimeStamp { get; set; }

    private int _screenWidth;
    private int _screenHeight;
    private bool _resourcesCreated;

    /// <summary>
    /// Registers a texture builder.
    /// </summary>
    public void AddTexture(
        string name,
        Func<ResourceBuildParams, TextureResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (_textureBuilders.TryGetValue(name, out var existing) && existing != null)
        {
            throw new InvalidOperationException(
                $"A texture with the name '{name}' already exists in the resource set."
            );
        }
        _textureBuilders[name] = buildFunc is not null
            ? new BuildTextureFunction(buildFunc, dependsOnScreenSize)
            : null;
        if (_textureResources.TryGetValue(name, out var texture))
        {
            texture.Dispose();
        }
        _textureResources[name] = TextureResource.Null;
    }

    /// <summary>
    /// Registers a buffer builder.
    /// </summary>
    public void AddBuffer(
        string name,
        Func<ResourceBuildParams, BufferResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (_bufferBuilders.TryGetValue(name, out var existing) && existing != null)
        {
            throw new InvalidOperationException(
                $"A buffer with the name '{name}' already exists in the resource set."
            );
        }
        _bufferBuilders[name] = buildFunc is not null
            ? new BuildBufferFunction(buildFunc, dependsOnScreenSize)
            : null;
        if (_bufferResources.TryGetValue(name, out var buffer))
        {
            buffer.Dispose();
        }
        _bufferResources[name] = BufferResource.Null;
    }

    /// <summary>
    /// Ensures GPU resources are up-to-date for the current render context.
    /// </summary>
    public void EnsureResources(RenderContext context, bool isDirty)
    {
        if (isDirty || !_resourcesCreated)
        {
            _screenWidth = context.WindowSize.Width;
            _screenHeight = context.WindowSize.Height;
            CreateAllResources(context);
            return;
        }

        if (context.WindowSize.Width != _screenWidth || context.WindowSize.Height != _screenHeight)
        {
            _screenWidth = context.WindowSize.Width;
            _screenHeight = context.WindowSize.Height;
            OnScreenSizeChanged(context);
        }
    }

    /// <summary>
    /// Injects well-known system resources that are produced externally.
    /// </summary>
    public void SetupSystemResources(RenderContext context)
    {
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
            var tex = builder.Value.Value.Func(resourceParams);
            _textureResources[builder.Key] = tex;
            Textures[builder.Key] = tex;
        }
        _resourcesCreated = true;
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
                _textureResources[builder.Key]?.Dispose();
                Textures[builder.Key] = TextureHandle.Null;
                continue;
            }
            if (builder.Value.Value.DependsOnScreenSize)
            {
                _textureResources[builder.Key]?.Dispose();
                var tex = builder.Value.Value.Func(resourceParams);
                _textureResources[builder.Key] = tex;
                Textures[builder.Key] = tex;
            }
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

    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            DisposeResources();
            _disposed = true;
        }
    }
}
