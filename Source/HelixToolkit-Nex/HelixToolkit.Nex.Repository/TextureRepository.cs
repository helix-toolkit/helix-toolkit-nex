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

    public TextureRepository(IContext context, int maxEntries = 0, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    public static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath).ToLowerInvariant();

    public TextureRef GetOrCreateFromStream(string name, Stream stream, bool generateMipmaps = true, string? debugName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var texture = TextureCreator.CreateTextureFromStream(_context, stream, generateMipmaps, debugName: debugName ?? name);
        return StoreEntry(name, texture, debugName ?? name);
    }

    public TextureRef GetOrCreateFromFile(string filePath, bool generateMipmaps = true, string? debugName = null)
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
        var texture = TextureCreator.CreateTextureFromStream(_context, stream, generateMipmaps, debugName: resolvedDebugName);
        return StoreEntry(cacheKey, texture, resolvedDebugName);
    }

    public TextureRef GetOrCreateFromImage(string name, Image image, bool generateMipmaps = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var texture = TextureCreator.CreateTexture(_context, image, generateMipmaps, debugName: name);
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
            var (textureResource, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(
                _context,
                image,
                generateMipmaps,
                debugName: debugName ?? name
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
            var (textureResource, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(
                _context,
                image,
                generateMipmaps,
                debugName: resolvedDebugName
            );
            var textureRef = StoreEntry(cacheKey, textureResource, resolvedDebugName);
            await uploadHandle;
            return textureRef;
        }
    }

    public async Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image, bool generateMipmaps = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (TryGet(name, out var cached))
            return cached!.Ref;

        var (textureResource, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(
            _context,
            image,
            generateMipmaps,
            debugName: name
        );
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
