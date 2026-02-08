using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Graphics;
// Import global types from HelixToolkit.Nex.Graphics
using size_t = uint;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Represents a cache entry for a compiled shader module.
/// </summary>
public sealed class ShaderModuleCacheEntry : CacheEntry<ShaderModuleResource>
{
    /// <summary>
    /// The compiled shader module resource.
    /// </summary>
    public ShaderModuleResource ShaderModule => Resource;

    /// <summary>
    /// The shader stage of this module.
    /// </summary>
    public required ShaderStage Stage { get; init; }
}

/// <summary>
/// Statistics for the shader repository cache.
/// </summary>
[Obsolete("Use RepositoryStatistics instead.")]
public sealed class ShaderRepositoryStatistics
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
/// Thread-safe repository for caching compiled SPIR-V shader modules.
/// </summary>
/// <remarks>
/// This repository caches <see cref="ShaderModuleResource"/> objects created from an <see cref="IContext"/>,
/// providing automatic deduplication and lifecycle management for shader modules. It uses an LRU eviction
/// policy when the cache is full and supports optional expiration times.
/// <para>
/// Unlike <see cref="HelixToolkit.Nex.Shaders.ShaderCache"/> which caches preprocessed GLSL source code,
/// this repository caches the final compiled SPIR-V shader modules to avoid redundant GPU resource creation.
/// </para>
/// </remarks>
public sealed class ShaderRepository
    : Repository<string, ShaderModuleCacheEntry, ShaderModuleResource>, IShaderRepository
{
    private readonly IContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderRepository"/> class.
    /// </summary>
    /// <param name="services">Service provider</param>
    /// <param name="maxEntries">Maximum number of shader modules to cache (0 = unlimited). Defaults is 0.</param>
    /// <param name="expirationTime">Time before a cached entry expires. Defaults to no expiration.</param>
    public ShaderRepository(
        IServiceProvider services,
        int maxEntries = 0,
        TimeSpan? expirationTime = null
    )
        : base(maxEntries, expirationTime)
    {
        _context = services.GetRequiredService<IContext>();
    }

    /// <summary>
    /// Disposes a shader module entry.
    /// </summary>
    /// <param name="entry">The entry to dispose.</param>
    protected override void DisposeEntry(ShaderModuleCacheEntry entry)
    {
        entry.ShaderModule.Dispose();
    }

    /// <summary>
    /// Adds a reference to a shader module resource.
    /// </summary>
    /// <param name="resource">The resource to add a reference to.</param>
    protected override void AddResourceReference(ShaderModuleResource resource)
    {
        resource.AddReference();
    }

    /// <summary>
    /// Generates a unique cache key for a shader module.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="glslSource">The GLSL source code (if creating from GLSL).</param>
    /// <param name="spirvData">Pointer to SPIR-V bytecode (if creating from SPIR-V).</param>
    /// <param name="spirvSize">Size of SPIR-V bytecode in bytes.</param>
    /// <param name="defines">Optional shader defines.</param>
    /// <returns>A unique cache key string.</returns>
    public static string GenerateCacheKey(
        ShaderStage stage,
        string? glslSource = null,
        nint spirvData = default,
        size_t spirvSize = 0,
        ShaderDefine[]? defines = null
    )
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append(stage.ToString());
        keyBuilder.Append('|');

        if (!string.IsNullOrEmpty(glslSource))
        {
            // Hash GLSL source
            keyBuilder.Append("GLSL|");
            keyBuilder.Append(ComputeHash(glslSource));
        }
        else if (spirvData != nint.Zero && spirvSize > 0)
        {
            // Hash SPIR-V bytecode
            keyBuilder.Append("SPIRV|");
            unsafe
            {
                var span = new ReadOnlySpan<byte>((void*)spirvData, (int)spirvSize);
                keyBuilder.Append(ComputeHash(span));
            }
        }
        else
        {
            throw new ArgumentException(
                "Either GLSL source or SPIR-V data must be provided for cache key generation."
            );
        }

        // Include defines in the key
        if (defines != null && defines.Length > 0)
        {
            keyBuilder.Append('|');
            foreach (var define in defines.OrderBy(d => d.Name))
            {
                keyBuilder.Append($"{define.Name}={define.Value ?? ""};");
            }
        }

        return ComputeHash(keyBuilder.ToString());
    }

    /// <summary>
    /// Gets or creates a shader module from GLSL source.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="glslSource">The GLSL source code.</param>
    /// <param name="defines">Optional shader defines.</param>
    /// <param name="debugName">Optional debug name for the shader module.</param>
    /// <returns>The shader module resource, either from cache or newly created.</returns>
    /// <exception cref="InvalidOperationException">Thrown if shader module creation fails.</exception>
    public ShaderModuleResource GetOrCreateFromGlsl(
        ShaderStage stage,
        string glslSource,
        ShaderDefine[]? defines = null,
        string? debugName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(glslSource);
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        var cacheKey = GenerateCacheKey(stage, glslSource: glslSource, defines: defines);

        // Try to get from cache
        if (TryGet(cacheKey, out var cached))
        {
            AddResourceReference(cached!.ShaderModule);
            return cached!.ShaderModule;
        }

        // Create new shader module
        var result = _context.CreateShaderModule(
            new ShaderModuleDesc
            {
                Stage = stage,
                DataType = ShaderDataType.Glsl,
                GlslSource = glslSource,
                Defines = defines ?? [],
                DebugName = debugName ?? string.Empty,
            },
            out var shaderModule
        );

        if (result != ResultCode.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to create shader module from GLSL: {result}"
            );
        }

        // Add to cache
        var entry = new ShaderModuleCacheEntry
        {
            Resource = shaderModule,
            Stage = stage,
            SourceHash = ComputeHash(glslSource),
            DebugName = debugName,
            AccessCount = 1, // Initialize to 1 since it's being accessed/used when created
        };

        Set(cacheKey, entry);
        AddResourceReference(shaderModule);
        return shaderModule;
    }

    /// <summary>
    /// Gets or creates a shader module from SPIR-V bytecode.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="spirvData">Pointer to SPIR-V bytecode.</param>
    /// <param name="spirvSize">Size of SPIR-V bytecode in bytes.</param>
    /// <param name="debugName">Optional debug name for the shader module.</param>
    /// <returns>The shader module resource, either from cache or newly created.</returns>
    /// <exception cref="InvalidOperationException">Thrown if shader module creation fails.</exception>
    public ShaderModuleResource GetOrCreateFromSpirv(
        ShaderStage stage,
        nint spirvData,
        size_t spirvSize,
        string? debugName = null
    )
    {
        ObjectDisposedException.ThrowIf(_context.IsDisposed, this);

        if (spirvData == nint.Zero || spirvSize == 0)
        {
            throw new ArgumentException("Invalid SPIR-V data.");
        }

        var cacheKey = GenerateCacheKey(stage, spirvData: spirvData, spirvSize: spirvSize);

        // Try to get from cache
        if (TryGet(cacheKey, out var cached))
        {
            AddResourceReference(cached!.ShaderModule);
            return cached!.ShaderModule;
        }

        // Create new shader module
        var result = _context.CreateShaderModule(
            new ShaderModuleDesc
            {
                Stage = stage,
                DataType = ShaderDataType.Spirv,
                Data = spirvData,
                DataSize = spirvSize,
                DebugName = debugName ?? string.Empty,
            },
            out var shaderModule
        );

        if (result != ResultCode.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to create shader module from SPIR-V: {result}"
            );
        }

        // Compute hash for SPIR-V data
        string sourceHash;
        unsafe
        {
            var span = new ReadOnlySpan<byte>((void*)spirvData, (int)spirvSize);
            sourceHash = ComputeHash(span);
        }

        // Add to cache
        var entry = new ShaderModuleCacheEntry
        {
            Resource = shaderModule,
            Stage = stage,
            SourceHash = sourceHash,
            DebugName = debugName,
            AccessCount = 1, // Initialize to 1 since it's being accessed/used when created
        };

        Set(cacheKey, entry);
        AddResourceReference(shaderModule);
        return shaderModule;
    }

    /// <summary>
    /// Computes SHA256 hash of a string.
    /// </summary>
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Computes SHA256 hash of a byte span.
    /// </summary>
    private static string ComputeHash(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }
}
