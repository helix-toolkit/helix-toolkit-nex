namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry for a sampler resource.
/// </summary>
public sealed class SamplerModuleCacheEntry : CacheEntry<SamplerResource>
{
    /// <summary>
    /// The sampler resource.
    /// </summary>
    public SamplerResource Sampler => Resource;
}

/// <summary>
/// Thread-safe repository for caching sampler state resources.
/// </summary>
/// <remarks>
/// This repository caches <see cref="SamplerResource"/> objects created from an <see cref="IContext"/>,
/// providing automatic deduplication and lifecycle management for samplers. It uses an LRU eviction
/// policy when the cache is full and supports optional expiration times.
/// <para>
/// The cache key is derived from the sampler description fields (excluding <c>DebugName</c>),
/// so samplers with identical settings but different debug names share the same cached resource.
/// </para>
/// </remarks>
public sealed class SamplerRepository
    : Repository<string, SamplerModuleCacheEntry, SamplerResource>,
        ISamplerRepository
{
    private readonly IContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SamplerRepository"/> class.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="maxEntries">Maximum number of samplers to cache (0 = unlimited). Default is 0.</param>
    /// <param name="expirationTime">Time before a cached entry expires. Defaults to no expiration.</param>
    public SamplerRepository(IContext context, int maxEntries = 0, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    /// <summary>
    /// Generates a unique cache key for a sampler description, excluding <c>DebugName</c>.
    /// </summary>
    /// <param name="desc">The sampler state description.</param>
    /// <returns>A unique cache key string.</returns>
    public static string GenerateCacheKey(SamplerStateDesc desc) =>
        $"{desc.MinFilter}|{desc.MagFilter}|{desc.MipMap}|{desc.WrapU}|{desc.WrapV}|{desc.WrapW}|{desc.DepthCompareOp}|{desc.MipLodMin}|{desc.MipLodMax}|{desc.MaxAnisotropic}|{desc.DepthCompareEnabled}";

    /// <summary>
    /// Gets or creates a sampler resource from the given description.
    /// </summary>
    /// <param name="desc">The sampler state description.</param>
    /// <returns>The sampler resource, either from cache or newly created.</returns>
    /// <exception cref="InvalidOperationException">Thrown if sampler creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    public SamplerResource GetOrCreate(SamplerStateDesc desc)
    {
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = GenerateCacheKey(desc);

        // Try to get from cache
        if (TryGet(cacheKey, out var cached))
        {
            AddResourceReference(cached!.Sampler);
            return cached!.Sampler;
        }

        // Create new sampler
        var result = _context.CreateSampler(desc, out var sampler);

        if (result != ResultCode.Ok)
        {
            throw new InvalidOperationException($"Failed to create sampler: {result}");
        }

        // Add to cache
        var entry = new SamplerModuleCacheEntry
        {
            Resource = sampler,
            SourceHash = cacheKey,
            DebugName = string.IsNullOrEmpty(desc.DebugName) ? null : desc.DebugName,
            AccessCount = 1,
        };

        Set(cacheKey, entry);
        AddResourceReference(sampler);
        HxDebug.Assert(sampler.Valid, "Sampler resource is not valid after creation.");
        return sampler;
    }

    /// <inheritdoc/>
    protected override void AddResourceReference(SamplerResource resource)
    {
        resource.AddReference();
    }

    /// <inheritdoc/>
    protected override void DisposeEntry(SamplerModuleCacheEntry entry)
    {
        entry.Sampler.Dispose();
    }
}
