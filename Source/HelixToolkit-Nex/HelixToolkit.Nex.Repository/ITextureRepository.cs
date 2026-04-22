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
    /// <returns>The texture resource, either from cache or newly created.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureResource GetOrCreateFromStream(string name, Stream stream, string? debugName = null);

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
    TextureResource GetOrCreateFromFile(string filePath, string? debugName = null);

    /// <summary>
    /// Attempts to retrieve a cached texture entry by its cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key (name or normalized file path).</param>
    /// <param name="entry">The cached texture entry if found.</param>
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
