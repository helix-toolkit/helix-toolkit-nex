using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// A no-op <see cref="ITextureRepository"/> used exclusively by <see cref="TextureRef.Null"/>.
/// </summary>
internal sealed class NullTextureRepository : ITextureRepository
{
    public static readonly NullTextureRepository Instance = new();

    private NullTextureRepository() { }

    public int Count => 0;

    public TextureRef GetOrCreateFromStream(string name, Stream stream, bool generateMipmaps = true, string? debugName = null) =>
        TextureRef.Null;

    public TextureRef GetOrCreateFromFile(string filePath, bool generateMipmaps = true, string? debugName = null) =>
        TextureRef.Null;

    public TextureRef GetOrCreateFromImage(string name, Image image, bool generateMipmaps = true) => TextureRef.Null;

    public Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    ) => Task.FromResult(TextureRef.Null);

    public Task<TextureRef> GetOrCreateFromFileAsync(string filePath, bool generateMipmaps = true, string? debugName = null) =>
        Task.FromResult(TextureRef.Null);

    public Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image, bool generateMipmaps = true) =>
        Task.FromResult(TextureRef.Null);

    public bool Remove(string key) => false;

    public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
    {
        entry = null;
        return false;
    }

    public void Clear() { }

    public int CleanupExpired() => 0;

    public RepositoryStatistics GetStatistics() =>
        new()
        {
            TotalEntries = 0,
            MaxEntries = 0,
            TotalHits = 0,
            TotalMisses = 0,
        };

    public void Dispose() { }
}

/// <summary>
/// A lightweight wrapper that holds a live <see cref="TextureResource"/> and exposes an
/// <see cref="OnDisposed"/> event for push notification when the resource is disposed.
/// </summary>
public sealed class TextureRef
{
    /// <summary>Gets the cache key that identifies this texture in the repository.</summary>
    public string Key { get; }

    /// <summary>Gets the repository back-reference.</summary>
    public ITextureRepository Repository { get; }

    /// <summary>The live GPU texture resource held internally.</summary>
    internal TextureResource Resource;

    /// <summary>Fires synchronously when <see cref="DisposeResource"/> is called.</summary>
    public event Action? OnDisposed;

    public bool Valid => Resource.Valid;

    /// <summary>
    /// Initializes a new instance of <see cref="TextureRef"/>.
    /// </summary>
    internal TextureRef(string key, ITextureRepository repository, TextureResource resource)
    {
        Key = key;
        Repository = repository;
        Resource = resource;
    }

    /// <summary>
    /// Returns the current GPU handle for this texture. Always returns <c>_resource.Handle</c> directly.
    /// </summary>
    public Handle<Texture> GetHandle() => Resource.Handle;

    /// <summary>
    /// Disposes the internally held resource and fires <see cref="OnDisposed"/> synchronously.
    /// Called by the repository's DisposeEntry only.
    /// </summary>
    internal void DisposeResource()
    {
        Resource.Dispose();
        OnDisposed?.Invoke();
    }

    // IDisposable is intentionally NOT implemented on TextureRef.
    // The repository is the sole owner of the underlying resource.
    // To release a texture, call TextureRepository.Remove(key).

    /// <summary>
    /// A sentinel <see cref="TextureRef"/> instance that represents the absence of a texture.
    /// </summary>
    public static readonly TextureRef Null = new(
        string.Empty,
        NullTextureRepository.Instance,
        TextureResource.Null
    );

    public static implicit operator uint(TextureRef obj)
    {
        return obj?.GetHandle().Index ?? 0;
    }
}
