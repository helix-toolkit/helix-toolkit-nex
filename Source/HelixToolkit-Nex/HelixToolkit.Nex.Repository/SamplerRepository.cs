namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry for a sampler resource.
/// </summary>
public sealed class SamplerModuleCacheEntry : CacheEntry<SamplerRef>
{
    /// <summary>The canonical SamplerRef for this cache entry.</summary>
    public SamplerRef Ref => Resource;
}

/// <summary>
/// Thread-safe repository for caching sampler state resources.
/// </summary>
public sealed class SamplerRepository
    : Repository<string, SamplerModuleCacheEntry, SamplerRef>,
        ISamplerRepository
{
    private readonly IContext _context;

    public SamplerRepository(IContext context, int maxEntries = 0, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    public static string GenerateCacheKey(SamplerStateDesc desc) =>
        $"{desc.MinFilter}|{desc.MagFilter}|{desc.MipMap}|{desc.WrapU}|{desc.WrapV}|{desc.WrapW}|{desc.DepthCompareOp}|{desc.MipLodMin}|{desc.MipLodMax}|{desc.MaxAnisotropic}|{desc.DepthCompareEnabled}";

    public SamplerRef GetOrCreate(SamplerStateDesc desc)
    {
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = GenerateCacheKey(desc);

        if (TryGet(cacheKey, out var cached))
            return cached!.Ref;

        var result = _context.CreateSampler(desc, out var sampler);
        if (result != ResultCode.Ok)
            throw new InvalidOperationException($"Failed to create sampler: {result}");

        return StoreEntry(
            cacheKey,
            sampler,
            string.IsNullOrEmpty(desc.DebugName) ? null : desc.DebugName
        );
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

    protected override void AddResourceReference(SamplerRef resource) { }

    protected override void DisposeEntry(SamplerModuleCacheEntry entry)
    {
        entry.Ref.DisposeResource();
    }

    private SamplerRef StoreEntry(string cacheKey, SamplerResource sampler, string? debugName)
    {
        var samplerRef = new SamplerRef(cacheKey, this, sampler);
        var entry = new SamplerModuleCacheEntry
        {
            Resource = samplerRef,
            SourceHash = cacheKey,
            DebugName = debugName,
            AccessCount = 1,
        };
        Set(cacheKey, entry);
        HxDebug.Assert(sampler.Valid, "Sampler resource is not valid after creation.");
        return samplerRef;
    }
}
