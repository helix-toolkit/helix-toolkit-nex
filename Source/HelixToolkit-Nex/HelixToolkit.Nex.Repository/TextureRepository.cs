using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry for a GPU texture resource.
/// </summary>
public sealed class TextureCacheEntry : CacheEntry<TextureResource>
{
    /// <summary>
    /// The GPU texture resource.
    /// </summary>
    public TextureResource Texture => Resource;
}

/// <summary>
/// Thread-safe repository for caching GPU texture resources.
/// </summary>
/// <remarks>
/// Textures can be created from:
/// <list type="bullet">
/// <item><description>A memory <see cref="Stream"/> with a caller-supplied name as the cache key.</description></item>
/// <item><description>A file path on disk — the normalized absolute path is used as the cache key.</description></item>
/// </list>
/// The repository uses an LRU eviction policy when the cache is full and supports optional
/// time-based expiration. All public members are thread-safe.
/// </remarks>
public sealed class TextureRepository
    : Repository<string, TextureCacheEntry, TextureResource>,
        ITextureRepository
{
    private readonly IContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureRepository"/> class.
    /// </summary>
    /// <param name="context">The graphics context used to create GPU textures.</param>
    /// <param name="maxEntries">Maximum number of textures to cache (0 = unlimited). Defaults to 0.</param>
    /// <param name="expirationTime">Time before a cached entry expires. Defaults to no expiration.</param>
    public TextureRepository(IContext context, int maxEntries = 0, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    /// <summary>
    /// Returns a normalized, case-insensitive absolute path suitable for use as a file cache key.
    /// </summary>
    /// <param name="filePath">The raw file path supplied by the caller.</param>
    /// <returns>Normalized absolute file path.</returns>
    public static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath).ToLowerInvariant();

    /// <summary>
    /// Gets or creates a GPU texture from a memory stream, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">
    /// A unique name that identifies this texture in the cache.
    /// If a texture with the same name already exists it is returned directly without re-decoding the stream.
    /// </param>
    /// <param name="stream">The stream containing the encoded image data.</param>
    /// <param name="debugName">Optional debug name forwarded to the GPU resource.</param>
    /// <returns>The texture resource, either from cache or newly created.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    public TextureResource GetOrCreateFromStream(string name, Stream stream, string? debugName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
        {
            AddResourceReference(cached!.Texture);
            return cached!.Texture;
        }

        var texture = TextureCreator.CreateTextureFromStream(_context, stream, debugName ?? name);
        return StoreEntry(name, texture, debugName ?? name);
    }

    /// <summary>
    /// Gets or creates a GPU texture by loading a file from the file system.
    /// The normalized absolute path is used as the cache key.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="debugName">
    /// Optional debug name forwarded to the GPU resource.
    /// Defaults to the file name when <c>null</c>.
    /// </param>
    /// <returns>The texture resource, either from cache or newly created.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the file cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    public TextureResource GetOrCreateFromFile(string filePath, string? debugName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = NormalizeFilePath(filePath);

        if (TryGet(cacheKey, out var cached))
        {
            AddResourceReference(cached!.Texture);
            return cached!.Texture;
        }

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Texture file not found: '{filePath}'", filePath);

        using var stream = File.OpenRead(filePath);
        var resolvedDebugName = debugName ?? Path.GetFileName(filePath);
        var texture = TextureCreator.CreateTextureFromStream(_context, stream, resolvedDebugName);
        return StoreEntry(cacheKey, texture, resolvedDebugName);
    }

    /// <inheritdoc/>
    protected override void AddResourceReference(TextureResource resource)
    {
        resource.AddReference();
    }

    /// <inheritdoc/>
    protected override void DisposeEntry(TextureCacheEntry entry)
    {
        entry.Texture.Dispose();
    }

    private TextureResource StoreEntry(string cacheKey, TextureResource texture, string debugName)
    {
        var entry = new TextureCacheEntry
        {
            Resource = texture,
            SourceHash = cacheKey,
            DebugName = debugName,
            AccessCount = 1,
        };

        Set(cacheKey, entry);
        AddResourceReference(texture);
        HxDebug.Assert(texture.Valid, "Texture resource is not valid after creation.");
        return texture;
    }
}
