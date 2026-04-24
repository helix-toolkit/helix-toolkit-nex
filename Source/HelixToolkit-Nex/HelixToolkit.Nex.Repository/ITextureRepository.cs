using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Interface for a thread-safe repository that caches GPU texture resources.
/// </summary>
/// <remarks>
/// This interface provides access to texture caching functionality, enabling:
/// <list type="bullet">
/// <item><description>Automatic deduplication of texture resources</description></item>
/// <item><description>LRU eviction policy for memory management</description></item>
/// <item><description>Cache statistics and monitoring</description></item>
/// <item><description>Thread-safe concurrent access</description></item>
/// </list>
/// Textures are keyed by a caller-supplied name (for stream sources) or by the normalized
/// absolute file path (for file-system sources).
/// <para>
/// Callers receive a <see cref="TextureRef"/> wrapper instead of a raw <see cref="TextureResource"/>.
/// The wrapper lazily re-fetches the GPU handle from the repository when the cached handle becomes stale,
/// enabling transparent hot-swap without manual reference-count management.
/// </para>
/// </remarks>
public interface ITextureRepository : IDisposable
{
    /// <summary>
    /// Gets the number of cached textures.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets or creates a GPU texture from a memory stream, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">
    /// A unique name that identifies this texture in the cache.
    /// If a texture with the same name already exists it is returned directly without re-decoding the stream.
    /// </param>
    /// <param name="stream">The stream containing the encoded image data.</param>
    /// <param name="debugName">Optional debug name forwarded to the GPU resource.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromStream(string name, Stream stream, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture by loading a file from the file system.
    /// The normalized absolute path is used as the cache key.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="debugName">
    /// Optional debug name forwarded to the GPU resource.
    /// Defaults to the file name when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> is the normalized absolute path
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the file cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromFile(string filePath, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture from an already decoded image, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">A unique name that identifies this texture in the cache.</param>
    /// <param name="image">The decoded image data.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the image cannot be used to create a texture.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromImage(string name, Image image);

    /// <summary>
    /// Gets or creates a GPU texture from a memory stream asynchronously, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">
    /// A unique name that identifies this texture in the cache.
    /// If a texture with the same name already exists it is returned directly without re-decoding the stream.
    /// </param>
    /// <param name="stream">The stream containing the encoded image data.</param>
    /// <param name="debugName">Optional debug name forwarded to the GPU resource.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        string? debugName = null
    );

    /// <summary>
    /// Gets or creates a GPU texture by loading a file from the file system asynchronously.
    /// The normalized absolute path is used as the cache key.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="debugName">
    /// Optional debug name forwarded to the GPU resource.
    /// Defaults to the file name when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> is the normalized absolute path
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromFileAsync(string filePath, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture from an already decoded image asynchronously, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">A unique name that identifies this texture in the cache.</param>
    /// <param name="image">The decoded image data.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image);

    /// <summary>
    /// Removes and disposes the texture stored under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The cache key of the texture to remove.</param>
    /// <returns>
    /// <c>true</c> if the texture was found and removed; <c>false</c> if the key did not exist.
    /// </returns>
    /// <remarks>
    /// After this call, any <see cref="TextureRef"/> previously returned for <paramref name="key"/>
    /// will return an invalid handle on the next <see cref="TextureRef.GetHandle()"/> call.
    /// </remarks>
    bool Remove(string key);

    /// <summary>
    /// Attempts to retrieve a cached texture entry by its cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key (name or normalized file path).</param>
    /// <param name="entry">The cached texture entry if found. Access the texture via <see cref="TextureCacheEntry.Ref"/>.</param>
    /// <returns><c>true</c> if the texture was found in cache; otherwise, <c>false</c>.</returns>
    bool TryGet(string cacheKey, out TextureCacheEntry? entry);

    /// <summary>
    /// Clears all cached textures and disposes of them.
    /// </summary>
    void Clear();

    /// <summary>
    /// Removes and disposes expired texture entries.
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    int CleanupExpired();

    /// <summary>
    /// Gets cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns>Statistics about the texture repository cache.</returns>
    RepositoryStatistics GetStatistics();
}
