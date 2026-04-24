namespace HelixToolkit.Nex.Repository;

/// <summary>
/// A no-op <see cref="ISamplerRepository"/> used exclusively by <see cref="SamplerRef.Null"/>.
/// </summary>
internal sealed class NullSamplerRepository : ISamplerRepository
{
    public static readonly NullSamplerRepository Instance = new();

    private NullSamplerRepository() { }

    public int Count => 0;

    public SamplerRef GetOrCreate(SamplerStateDesc desc) => SamplerRef.Null;

    public bool Remove(string key) => false;

    public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
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
/// A lightweight wrapper that holds a live <see cref="SamplerResource"/> and exposes an
/// <see cref="OnDisposed"/> event for push notification when the resource is disposed.
/// </summary>
public sealed class SamplerRef : IDisposable
{
    /// <summary>Gets the cache key that identifies this sampler in the repository.</summary>
    public string Key { get; }

    /// <summary>Gets the repository back-reference.</summary>
    public ISamplerRepository Repository { get; }

    internal SamplerResource Resource;

    /// <summary>Raised when <see cref="DisposeResource"/> is called.</summary>
    public event Action? OnDisposed;

    internal SamplerRef(string key, ISamplerRepository repository, SamplerResource resource)
    {
        Key = key;
        Repository = repository;
        Resource = resource;
    }

    /// <summary>Returns the current GPU handle for this sampler.</summary>
    public Handle<Sampler> GetHandle() => Resource.Handle;

    public bool Valid => Resource.Valid;

    /// <summary>Disposes the underlying <see cref="SamplerResource"/> and raises <see cref="OnDisposed"/>.</summary>
    internal void DisposeResource()
    {
        Resource.Dispose();
        OnDisposed?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose() => DisposeResource();

    /// <summary>
    /// A sentinel <see cref="SamplerRef"/> that represents the absence of a sampler.
    /// <see cref="GetHandle()"/> on this instance always returns an invalid handle and never throws.
    /// </summary>
    public static readonly SamplerRef Null = new(
        string.Empty,
        NullSamplerRepository.Instance,
        SamplerResource.Null
    );

    public static implicit operator uint(SamplerRef obj)
    {
        return obj?.GetHandle().Index ?? 0;
    }
}
