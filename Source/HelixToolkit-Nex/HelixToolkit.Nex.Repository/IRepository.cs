namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Interface for a thread-safe generic repository that caches resources with LRU eviction policy.
/// </summary>
/// <typeparam name="TKey">The type of the cache key.</typeparam>
/// <typeparam name="TEntry">The type of cache entry, must inherit from <see cref="CacheEntry{TResource}"/>.</typeparam>
/// <typeparam name="TResource">The type of cached resource.</typeparam>
/// <remarks>
/// This interface provides a common contract for resource caching with:
/// <list type="bullet">
/// <item><description>Thread-safe concurrent access</description></item>
/// <item><description>LRU (Least Recently Used) eviction policy</description></item>
/// <item><description>Optional time-based expiration</description></item>
/// <item><description>Cache statistics and monitoring</description></item>
/// <item><description>Automatic resource lifecycle management</description></item>
/// </list>
/// </remarks>
public interface IRepository<TKey, TEntry, TResource> : IDisposable
    where TKey : notnull
    where TEntry : CacheEntry<TResource>
    where TResource : class
{
    /// <summary>
    /// Gets the number of cached resources.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to retrieve a cached resource entry.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="entry">The cached resource entry if found.</param>
    /// <returns><c>true</c> if the resource was found in cache; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// This method is thread-safe and updates access statistics when a cached entry is found.
    /// If the entry has expired, it will be automatically removed from the cache.
    /// </remarks>
    bool TryGet(TKey cacheKey, out TEntry? entry);

    /// <summary>
    /// Clears all cached resources and disposes of them.
    /// </summary>
    /// <remarks>
    /// This method is thread-safe and will dispose all cached resources before clearing the cache.
    /// Cache statistics are reset to zero after clearing.
    /// </remarks>
    void Clear();

    /// <summary>
    /// Removes and disposes expired resource entries.
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    /// <remarks>
    /// If no expiration time was configured, this method returns 0 without performing any cleanup.
    /// This method is thread-safe.
    /// </remarks>
    int CleanupExpired();

    /// <summary>
    /// Gets cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns>Statistics about the repository cache.</returns>
    /// <remarks>
    /// The statistics include:
    /// <list type="bullet">
    /// <item><description>Total entries and maximum capacity</description></item>
    /// <item><description>Cache hits and misses</description></item>
    /// <item><description>Hit rate percentage</description></item>
    /// <item><description>Access count statistics</description></item>
    /// <item><description>Entry age information</description></item>
    /// </list>
    /// </remarks>
    RepositoryStatistics GetStatistics();
}
