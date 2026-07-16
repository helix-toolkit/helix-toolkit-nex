using System.Collections.Concurrent;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry for a GPU texture resource.
/// </summary>
public sealed class TextureCacheEntry : CacheEntry<TextureRef>
{
    /// <summary>The canonical TextureRef for this cache entry.</summary>
    public TextureRef Ref => Resource;
}

/// <summary>
/// Thread-safe repository for caching GPU texture resources.
/// </summary>
public sealed class TextureRepository
    : Repository<string, TextureCacheEntry, TextureRef>,
        ITextureRepository
{
    private readonly IContext _context;

    /// <summary>
    /// Pending GPU mipmap-generation requests. Enqueued from the (possibly background)
    /// texture-creation paths and drained on the render thread by
    /// <see cref="ProcessPendingMipmapGeneration"/>. <see cref="IContext.GenerateMipmap(in TextureHandle, out uint)"/>
    /// performs an immediate command-buffer submission and must only be invoked on the render thread.
    /// Each entry carries a completion source that is signalled once generation has run (or been
    /// discarded), enabling callers to await GPU readiness via <see cref="WhenMipmapReadyAsync"/>.
    /// </summary>
    private readonly ConcurrentQueue<PendingMipmap> _pendingMipmaps = new();

    /// <summary>
    /// Maps a texture handle awaiting mipmap generation to the task that completes when generation
    /// has run. Entries are removed once drained, so a missing handle means "already ready".
    /// </summary>
    private readonly ConcurrentDictionary<TextureHandle, Task> _mipmapReady = new();

    private readonly record struct PendingMipmap(
        TextureHandle Handle,
        TaskCompletionSource Completion
    );

    public TextureRepository(IContext context, int maxEntries = 0, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    /// <summary>
    /// Number of texture handles awaiting GPU mipmap generation on the render thread.
    /// </summary>
    public int PendingMipmapCount => _pendingMipmaps.Count;

    /// <summary>
    /// Enqueues a texture handle for deferred GPU mipmap generation. Thread-safe; the actual
    /// generation runs later on the render thread via <see cref="ProcessPendingMipmapGeneration"/>.
    /// </summary>
    private void ScheduleMipmapGeneration(TextureHandle handle)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Record the readiness task before enqueuing so a concurrent drain always finds it.
        _mipmapReady[handle] = tcs.Task;
        _pendingMipmaps.Enqueue(new PendingMipmap(handle, tcs));
    }

    /// <inheritdoc/>
    public Task WhenMipmapReadyAsync(TextureHandle handle) =>
        _mipmapReady.TryGetValue(handle, out var task) ? task : Task.CompletedTask;

    /// <inheritdoc/>
    public void ProcessPendingMipmapGeneration()
    {
        // Must be called on the engine render thread. IContext.GenerateMipmap acquires and submits
        // an immediate command buffer, so it cannot run from the background upload continuations.
        while (_pendingMipmaps.TryDequeue(out var pending))
        {
            try
            {
                // Skip generation if the context is gone or the handle is invalid, but still
                // signal completion so awaiters never hang.
                if (!_context.IsDisposed && !pending.Handle.Empty)
                {
                    _context.GenerateMipmap(pending.Handle, out _);
                }
            }
            finally
            {
                _mipmapReady.TryRemove(pending.Handle, out _);
                pending.Completion.TrySetResult();
            }
        }
    }

    public static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath).ToLowerInvariant();

    public TextureRef GetOrCreateFromStream(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var texture = TextureCreator.CreateTextureFromStream(
            _context,
            stream,
            generateMipmaps,
            debugName: debugName ?? name,
            scheduleMipmapGeneration: ScheduleMipmapGeneration
        );
        return StoreEntry(name, texture, debugName ?? name);
    }

    public TextureRef GetOrCreateFromFile(
        string filePath,
        bool generateMipmaps = true,
        string? debugName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = NormalizeFilePath(filePath);

        if (TryGet(cacheKey, out var cached))
            return cached!.Ref;

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Texture file not found: '{filePath}'", filePath);

        using var stream = File.OpenRead(filePath);
        var resolvedDebugName = debugName ?? Path.GetFileName(filePath);
        var texture = TextureCreator.CreateTextureFromStream(
            _context,
            stream,
            generateMipmaps,
            debugName: resolvedDebugName,
            scheduleMipmapGeneration: ScheduleMipmapGeneration
        );
        return StoreEntry(cacheKey, texture, resolvedDebugName);
    }

    public TextureRef GetOrCreateFromImage(string name, Image image, bool generateMipmaps = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var texture = TextureCreator.CreateTexture(
            _context,
            image,
            generateMipmaps,
            debugName: name,
            scheduleMipmapGeneration: ScheduleMipmapGeneration
        );
        return StoreEntry(name, texture, name);
    }

    public async Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException("Failed to load image from stream");

        using (image)
        {
            var (result, textureResource, uploadHandle) =
                TextureCreator.CreateTextureAsyncWithResource(
                    _context,
                    image,
                    generateMipmaps,
                    debugName: debugName ?? name,
                    scheduleMipmapGeneration: ScheduleMipmapGeneration
                );
            if (result != ResultCode.Ok)
                throw new InvalidOperationException(
                    $"Failed to create GPU texture '{name}': {result}"
                );
            var textureRef = StoreEntry(name, textureResource, debugName ?? name);
            await uploadHandle;
            return textureRef;
        }
    }

    public async Task<TextureRef> GetOrCreateFromFileAsync(
        string filePath,
        bool generateMipmaps = true,
        string? debugName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = NormalizeFilePath(filePath);

        if (TryGet(cacheKey, out var cached))
            return cached!.Ref;

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Texture file not found: '{filePath}'", filePath);

        using var stream = File.OpenRead(filePath);
        var resolvedDebugName = debugName ?? Path.GetFileName(filePath);
        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException("Failed to load image from file");

        using (image)
        {
            var (result, textureResource, uploadHandle) =
                TextureCreator.CreateTextureAsyncWithResource(
                    _context,
                    image,
                    generateMipmaps,
                    debugName: resolvedDebugName,
                    scheduleMipmapGeneration: ScheduleMipmapGeneration
                );
            if (result != ResultCode.Ok)
                throw new InvalidOperationException(
                    $"Failed to create GPU texture '{resolvedDebugName}': {result}"
                );
            var textureRef = StoreEntry(cacheKey, textureResource, resolvedDebugName);
            await uploadHandle;
            return textureRef;
        }
    }

    public async Task<TextureRef> GetOrCreateFromImageAsync(
        string name,
        Image image,
        bool generateMipmaps = true
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var (result, textureResource, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(
            _context,
            image,
            generateMipmaps,
            debugName: name,
            scheduleMipmapGeneration: ScheduleMipmapGeneration
        );
        if (result != ResultCode.Ok)
            throw new InvalidOperationException($"Failed to create GPU texture '{name}': {result}");
        var textureRef = StoreEntry(name, textureResource, name);
        await uploadHandle;
        return textureRef;
    }

    public bool Remove(string key)
    {
        if (TryRemoveFromCache(key, out var removed) && removed is not null)
        {
            DisposeEntry(removed);
            return true;
        }
        return false;
    }

    protected override void AddResourceReference(TextureRef resource) { }

    protected override void DisposeEntry(TextureCacheEntry entry)
    {
        entry.Ref.DisposeResource();
    }

    private TextureRef StoreEntry(string cacheKey, TextureResource texture, string debugName)
    {
        var textureRef = new TextureRef(cacheKey, this, texture);
        var entry = new TextureCacheEntry
        {
            Resource = textureRef,
            SourceHash = cacheKey,
            DebugName = debugName,
            AccessCount = 1,
        };
        Set(cacheKey, entry);
        HxDebug.Assert(texture.Valid, "Texture resource is not valid after creation.");
        return textureRef;
    }
}
