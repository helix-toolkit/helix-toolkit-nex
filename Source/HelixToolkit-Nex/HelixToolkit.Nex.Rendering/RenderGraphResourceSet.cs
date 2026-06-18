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
/// Thrown when a render-graph resource fails to allocate while the
/// <see cref="RenderGraphResourceSet"/> is building its buffers/textures.
/// <para>
/// When a buffer allocation fails, buffer setup is aborted and any buffers that were
/// already allocated during the same setup pass are released before this exception is
/// surfaced. The failed resource is identified by its logical name via
/// <see cref="ResourceName"/> (e.g. <c>BufLightIndexT</c>) so callers can diagnose which
/// allocation aborted setup.
/// </para>
/// </summary>
public sealed class RenderGraphResourceAllocationException : Exception
{
    /// <summary>The logical name of the resource whose allocation failed.</summary>
    public string ResourceName { get; }

    /// <summary>The kind of resource (buffer or texture) that failed to allocate.</summary>
    public ResourceType ResourceType { get; }

    public RenderGraphResourceAllocationException(
        string resourceName,
        ResourceType resourceType,
        Exception? innerException = null
    )
        : base(
            $"Failed to allocate render graph {resourceType.ToString().ToLowerInvariant()} '{resourceName}'. "
                + "Buffer setup was aborted and partially allocated buffers were released.",
            innerException
        )
    {
        ResourceName = resourceName;
        ResourceType = resourceType;
    }
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
    /// Attempts to retrieve a texture handle associated with the specified name.
    /// </summary>
    /// <param name="name">The name of the texture to locate. Cannot be null.</param>
    /// <param name="handle">When this method returns, contains the handle of the texture if found; otherwise, the default value for <see
    /// cref="TextureHandle"/>.</param>
    /// <returns><see langword="true"/> if a texture with the specified name is found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetTexture(string name, out TextureHandle handle)
    {
        return Textures.TryGetValue(name, out handle);
    }

    /// <summary>
    /// Attempts to retrieve a buffer handle associated with the specified name.
    /// </summary>
    /// <param name="name">The name of the buffer to locate. Cannot be null.</param>
    /// <param name="handle">When this method returns, contains the buffer handle associated with the specified name, if found; otherwise,
    /// the default value.</param>
    /// <returns>true if a buffer with the specified name is found; otherwise, false.</returns>
    public bool TryGetBuffer(string name, out BufferHandle handle)
    {
        return Buffers.TryGetValue(name, out handle);
    }

    /// <summary>
    /// Ensures GPU resources are up-to-date for the current render context.
    /// </summary>
    /// <param name="context">The current render context providing necessary information for resource creation.</param>
    /// <param name="isDirty">Indicates whether all resources should be recreated regardless of screen size changes.</param>
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

        Buffers[SystemBufferNames.BufferDirectionalLight] =
            context.Data?.DirectionalLights.Buffer ?? BufferResource.Null;

        Buffers[SystemBufferNames.BufferLights] =
            context.Data?.Lights.Buffer ?? BufferResource.Null;

        Buffers[SystemBufferNames.BufferPBRProperties] =
            context.Data?.PBRPropertiesBuffer.Buffer ?? BufferResource.Null;

        Buffers[SystemBufferNames.BufferMeshInfo] =
            context.Data?.MeshInfos.Buffer ?? BufferResource.Null;

        Buffers[SystemBufferNames.BufferNodeInfo] =
            context.Data?.NodeInfos.Buffer ?? BufferResource.Null;
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

        // Track buffers successfully allocated during THIS setup so they can be released
        // if a later allocation fails. If any buffer allocation fails, buffer setup is
        // aborted, the partially allocated buffers are released, and an error identifying
        // the failed buffer by its logical name is surfaced (Requirement 6.7).
        var allocatedThisSetup = new List<BufferResource>();
        foreach (var builder in _bufferBuilders)
        {
            if (builder.Value == null)
            {
                Buffers[builder.Key] = BufferHandle.Null;
                continue;
            }

            BufferResource buf;
            try
            {
                buf = builder.Value.Value.Func(resourceParams);
            }
            catch (Exception ex) when (ex is not RenderGraphResourceAllocationException)
            {
                AbortBufferSetup(allocatedThisSetup, builder.Key, ex);
                throw new RenderGraphResourceAllocationException(
                    builder.Key,
                    ResourceType.Buffer,
                    ex
                );
            }

            // A null or empty (invalid-handle) buffer indicates the allocation failed even
            // though no exception was thrown.
            if (buf is null || buf.Empty)
            {
                AbortBufferSetup(allocatedThisSetup, builder.Key, null);
                throw new RenderGraphResourceAllocationException(builder.Key, ResourceType.Buffer);
            }

            allocatedThisSetup.Add(buf);
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

    /// <summary>
    /// Aborts an in-progress buffer setup: releases every buffer that was successfully
    /// allocated during the current setup pass and clears the partially populated resource
    /// dictionaries so a half-initialized buffer set is never bound. Used when a buffer
    /// allocation fails (Requirement 6.7).
    /// </summary>
    /// <param name="allocatedThisSetup">Buffers allocated so far in the current setup pass.</param>
    /// <param name="failedName">The logical name of the buffer whose allocation failed.</param>
    /// <param name="error">The exception that caused the failure, if any.</param>
    private void AbortBufferSetup(
        List<BufferResource> allocatedThisSetup,
        string failedName,
        Exception? error
    )
    {
        _logger.LogError(
            error,
            "Allocation of render graph buffer '{BUFFER}' failed. Aborting buffer setup and "
                + "releasing {COUNT} partially allocated buffer(s).",
            failedName,
            allocatedThisSetup.Count
        );

        foreach (var buf in allocatedThisSetup)
        {
            buf.Dispose();
        }
        allocatedThisSetup.Clear();

        // Drop the partially populated set so nothing half-built can be bound.
        Buffers.Clear();
        Textures.Clear();
        _resourcesCreated = false;
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
