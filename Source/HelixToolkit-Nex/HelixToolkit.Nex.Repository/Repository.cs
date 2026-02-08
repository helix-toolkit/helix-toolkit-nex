using System.Collections.Concurrent;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry in a generic repository.
/// </summary>
/// <typeparam name="TResource">The type of the cached resource.</typeparam>
public abstract class CacheEntry<TResource> where TResource : class, IDisposable
{
    /// <summary>
    /// The cached resource.
    /// </summary>
    public required TResource Resource { get; init; }

    /// <summary>
    /// Hash of the resource source or data.
    /// </summary>
    public required string SourceHash { get; init; }

    /// <summary>
    /// Timestamp when the entry was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Last access time.
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    private int _accessCount;

    /// <summary>
    /// Number of times this entry has been accessed.
    /// </summary>
    public int AccessCount
    {
        get => Interlocked.CompareExchange(ref _accessCount, 0, 0);
        set => Interlocked.Exchange(ref _accessCount, value);
    }

    /// <summary>
    /// Optional debug name for the resource.
    /// </summary>
    public string? DebugName { get; init; }

    /// <summary>
    /// Atomically increments the access count.
    /// </summary>
    internal void IncrementAccessCount()
    {
        Interlocked.Increment(ref _accessCount);
    }
}

/// <summary>
/// Statistics for a repository cache.
/// </summary>
public sealed class RepositoryStatistics
{
    /// <summary>
    /// Total number of entries in the cache.
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Maximum number of entries allowed.
    /// </summary>
    public int MaxEntries { get; init; }

    /// <summary>
    /// Total number of cache hits.
    /// </summary>
    public long TotalHits { get; init; }

    /// <summary>
    /// Total number of cache misses.
    /// </summary>
    public long TotalMisses { get; init; }

    /// <summary>
    /// Cache hit rate as a percentage (0-100).
    /// </summary>
    public double HitRate =>
        TotalHits + TotalMisses > 0 ? (TotalHits * 100.0) / (TotalHits + TotalMisses) : 0;

    /// <summary>
    /// Total number of accesses across all entries.
    /// </summary>
    public long TotalAccessCount { get; init; }

    /// <summary>
    /// Average access count per entry.
    /// </summary>
    public double AverageAccessCount { get; init; }

    /// <summary>
    /// Timestamp of the oldest entry.
    /// </summary>
    public DateTime? OldestEntry { get; init; }

    /// <summary>
    /// Timestamp of the newest entry.
    /// </summary>
    public DateTime? NewestEntry { get; init; }
}

/// <summary>
/// Thread-safe generic repository for caching resources with LRU eviction policy.
/// </summary>
/// <typeparam name="TKey">The type of the cache key.</typeparam>
/// <typeparam name="TEntry">The type of cache entry, must inherit from <see cref="CacheEntry{TResource}"/>.</typeparam>
/// <typeparam name="TResource">The type of cached resource.</typeparam>
/// <remarks>
/// This repository provides automatic deduplication and lifecycle management for cached resources.
/// It uses an LRU eviction policy when the cache is full and supports optional expiration times.
/// </remarks>
public abstract class Repository<TKey, TEntry, TResource> : IRepository<TKey, TEntry, TResource>
    where TKey : notnull
    where TEntry : CacheEntry<TResource>
    where TResource : class, IDisposable
{
    private readonly ConcurrentDictionary<TKey, TEntry> _cache = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _expirationTime;
    private readonly ReaderWriterLockSlim _evictionLock = new();
    private long _cacheHits;
    private long _cacheMisses;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Repository{TKey, TEntry, TResource}"/> class.
    /// </summary>
    /// <param name="maxEntries">Maximum number of resources to cache (0 = unlimited). Defaults to 500.</param>
    /// <param name="expirationTime">Time before a cached entry expires. Defaults to no expiration.</param>
    protected Repository(int maxEntries = 500, TimeSpan? expirationTime = null)
    {
        _maxEntries = maxEntries;
        _expirationTime = expirationTime ?? TimeSpan.MaxValue;
    }

    /// <summary>
    /// Gets the number of cached resources.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Disposes a resource entry. Derived classes must implement this to properly dispose the resource.
    /// </summary>
    /// <param name="entry">The entry to dispose.</param>
    protected abstract void DisposeEntry(TEntry entry);

    /// <summary>
    /// Adds a reference to a resource. Derived classes must implement this if resources are reference-counted.
    /// </summary>
    /// <param name="resource">The resource to add a reference to.</param>
    protected abstract void AddResourceReference(TResource resource);

    /// <summary>
    /// Attempts to retrieve a cached resource.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="entry">The cached resource entry if found.</param>
    /// <returns><c>true</c> if the resource was found in cache; otherwise, <c>false</c>.</returns>
    public bool TryGet(TKey cacheKey, out TEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cache.TryGetValue(cacheKey, out var cachedEntry))
        {
            // Check if entry has expired
            if (DateTime.UtcNow - cachedEntry.CreatedAt > _expirationTime)
            {
                if (_cache.TryRemove(cacheKey, out var removed))
                {
                    DisposeEntry(removed);
                }
                Interlocked.Increment(ref _cacheMisses);
                entry = null;
                return false;
            }

            // Update access statistics
            cachedEntry.LastAccessedAt = DateTime.UtcNow;
            cachedEntry.IncrementAccessCount();
            Interlocked.Increment(ref _cacheHits);

            entry = cachedEntry;
            return true;
        }

        Interlocked.Increment(ref _cacheMisses);
        entry = null;
        return false;
    }

    /// <summary>
    /// Adds or updates a resource in the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="entry">The cache entry to add.</param>
    protected void Set(TKey cacheKey, TEntry entry)
    {
        // Check if we need to evict entries
        if (_maxEntries > 0 && _cache.Count >= _maxEntries)
        {
            EvictLeastRecentlyUsed();
        }

        _cache.AddOrUpdate(cacheKey, entry, (_, _) => entry);
    }

    /// <summary>
    /// Clears all cached resources and disposes of them.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _evictionLock.EnterWriteLock();
        try
        {
            foreach (var entry in _cache.Values)
            {
                DisposeEntry(entry);
            }
            _cache.Clear();
            Interlocked.Exchange(ref _cacheHits, 0);
            Interlocked.Exchange(ref _cacheMisses, 0);
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes and disposes expired resource entries.
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    public int CleanupExpired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_expirationTime == TimeSpan.MaxValue)
            return 0;

        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => now - kvp.Value.CreatedAt > _expirationTime)
            .Select(kvp => kvp.Key)
            .ToList();

        int removed = 0;
        foreach (var key in expiredKeys)
        {
            if (_cache.TryRemove(key, out var entry))
            {
                DisposeEntry(entry);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Evicts the least recently used resource from the cache.
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        _evictionLock.EnterWriteLock();
        try
        {
            var lruEntry = _cache
                .OrderBy(kvp => kvp.Value.LastAccessedAt)
                .ThenBy(kvp => kvp.Value.AccessCount)
                .FirstOrDefault();

            if (lruEntry.Key != null)
            {
                if (_cache.TryRemove(lruEntry.Key, out var removed))
                {
                    DisposeEntry(removed);
                }
            }
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns>Statistics about the repository cache.</returns>
    public RepositoryStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new RepositoryStatistics
        {
            TotalEntries = _cache.Count,
            MaxEntries = _maxEntries,
            TotalHits = Interlocked.Read(ref _cacheHits),
            TotalMisses = Interlocked.Read(ref _cacheMisses),
            TotalAccessCount = _cache.Values.Sum(e => e.AccessCount),
            AverageAccessCount = _cache.Count > 0 ? _cache.Values.Average(e => e.AccessCount) : 0,
            OldestEntry = _cache.Values.Any()
                ? _cache.Values.Min(e => e.CreatedAt)
                : (DateTime?)null,
            NewestEntry = _cache.Values.Any()
                ? _cache.Values.Max(e => e.CreatedAt)
                : (DateTime?)null,
        };
    }

    /// <summary>
    /// Disposes all cached resources and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var entry in _cache.Values)
        {
            DisposeEntry(entry);
        }
        _cache.Clear();

        _evictionLock.Dispose();

        GC.SuppressFinalize(this);
    }
}
