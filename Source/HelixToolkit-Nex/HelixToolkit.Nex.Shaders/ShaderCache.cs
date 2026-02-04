using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Cache entry for compiled shaders
/// </summary>
public class ShaderCacheEntry
{
    /// <summary>
    /// The processed shader source code
    /// </summary>
    public required string ProcessedSource { get; init; }

    /// <summary>
    /// Hash of the original source code
    /// </summary>
    public required string SourceHash { get; init; }

    /// <summary>
    /// Timestamp when the entry was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Last access time
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of times this entry has been accessed
    /// </summary>
    public int AccessCount { get; set; } = 0;
}

/// <summary>
/// Thread-safe shader cache that stores processed shader sources
/// </summary>
public class ShaderCache
{
    private readonly ConcurrentDictionary<string, ShaderCacheEntry> _cache = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _expirationTime;

    /// <summary>
    /// Creates a new shader cache
    /// </summary>
    /// <param name="maxEntries">Maximum number of entries to store (0 = unlimited)</param>
    /// <param name="expirationTime">Time before an entry expires (default: no expiration)</param>
    public ShaderCache(int maxEntries = 100, TimeSpan? expirationTime = null)
    {
        _maxEntries = maxEntries;
        _expirationTime = expirationTime ?? TimeSpan.MaxValue;
    }

    /// <summary>
    /// Generate a unique cache key from shader source and build options
    /// </summary>
    public static string GenerateCacheKey(
        string source,
        ShaderBuildOptions options,
        ShaderStage stage
    )
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append(stage.ToString());
        keyBuilder.Append('|');
        keyBuilder.Append(ComputeHash(source));
        keyBuilder.Append('|');
        keyBuilder.Append(options.StripComments);

        // Include defines in the key
        if (options.Defines.Count > 0)
        {
            keyBuilder.Append('|');
            foreach (var define in options.Defines.OrderBy(d => d.Key))
            {
                keyBuilder.Append($"{define.Key}={define.Value};");
            }
        }

        return ComputeHash(keyBuilder.ToString());
    }

    /// <summary>
    /// Try to get a cached shader
    /// </summary>
    public bool TryGet(string cacheKey, out ShaderCacheEntry? entry)
    {
        if (_cache.TryGetValue(cacheKey, out var cachedEntry))
        {
            // Check if entry has expired
            if (DateTime.UtcNow - cachedEntry.CreatedAt > _expirationTime)
            {
                _cache.TryRemove(cacheKey, out _);
                entry = null;
                return false;
            }

            // Update access statistics
            cachedEntry.LastAccessedAt = DateTime.UtcNow;
            cachedEntry.AccessCount++;

            entry = cachedEntry;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Add or update a cache entry
    /// </summary>
    public void Set(string cacheKey, string processedSource, string sourceHash)
    {
        // Check if we need to evict entries
        if (_maxEntries > 0 && _cache.Count >= _maxEntries)
        {
            EvictLeastRecentlyUsed();
        }

        var entry = new ShaderCacheEntry
        {
            ProcessedSource = processedSource,
            SourceHash = sourceHash,
            AccessCount = 1, // Initialize to 1 since it's being accessed/used when created
        };

        _cache.AddOrUpdate(cacheKey, entry, (_, _) => entry);
    }

    /// <summary>
    /// Clear all cached entries
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Get the number of entries in the cache
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Remove expired entries
    /// </summary>
    public int CleanupExpired()
    {
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
            if (_cache.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Evict the least recently used entry
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        var lruEntry = _cache
            .OrderBy(kvp => kvp.Value.LastAccessedAt)
            .ThenBy(kvp => kvp.Value.AccessCount)
            .FirstOrDefault();

        if (lruEntry.Key != null)
        {
            _cache.TryRemove(lruEntry.Key, out _);
        }
    }

    /// <summary>
    /// Compute SHA256 hash of a string
    /// </summary>
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            TotalEntries = _cache.Count,
            MaxEntries = _maxEntries,
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
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public int TotalEntries { get; init; }
    public int MaxEntries { get; init; }
    public int TotalAccessCount { get; init; }
    public double AverageAccessCount { get; init; }
    public DateTime? OldestEntry { get; init; }
    public DateTime? NewestEntry { get; init; }
}
