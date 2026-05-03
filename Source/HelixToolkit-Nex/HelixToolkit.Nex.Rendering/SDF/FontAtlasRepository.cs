using System.Reflection;

namespace HelixToolkit.Nex.Rendering.SDF;

/// <summary>
/// Thread-safe repository that caches <see cref="SDFFontAtlas"/> instances by name.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="Repository.TextureRepository"/>, this repository does not own GPU resources.
/// The underlying textures are owned by <see cref="ITextureRepository"/>; this class only caches
/// the <see cref="SDFFontAtlas"/> wrappers (texture index + sampler index + glyph metrics).
/// Calling <see cref="Clear"/> removes entries from the dictionary without disposing GPU resources.
/// </para>
/// </remarks>
public sealed class FontAtlasRepository : IFontAtlasRepository
{
    private readonly Dictionary<string, SDFFontAtlas> _cache = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_lock)
                return _cache.Count;
        }
    }

    /// <inheritdoc />
    public SDFFontAtlas GetOrCreateBuiltIn(
        BuildinFontAtlas atlasType,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = $"BuiltIn:{atlasType}";

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;
        }

        // Resolve the embedded resource names for this atlas type
        var (pngResource, jsonResource) = GetBuiltInResourceNames(atlasType);

        // Load outside lock to avoid holding it during I/O
        var assembly = typeof(SDFFontAtlasLoader).Assembly;
        var assemblyName = assembly.GetName().Name;

        // Load texture
        var fullPngName = $"{assemblyName}.{pngResource}";
        using var pngStream =
            assembly.GetManifestResourceStream(fullPngName)
            ?? throw new FileNotFoundException($"Embedded resource '{fullPngName}' not found.");
        var textureRef = textureRepository.GetOrCreateFromStream(
            $"FontAtlas:{key}",
            pngStream,
            generateMipmaps: false,
            debugName: $"SDFFont_Atlas_{atlasType}"
        );
        uint textureIndex = textureRef;

        // Create sampler
        var samplerRef = samplerRepository.GetOrCreate(SamplerStateDesc.LinearClampNoMipmap);
        uint samplerIndex = samplerRef;

        // Load descriptor
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource(jsonResource);
        var atlas = new SDFFontAtlas(textureIndex, samplerIndex, descriptor);

        lock (_lock)
        {
            // Double-check after loading
            if (_cache.TryGetValue(key, out var existing))
                return existing;
            _cache[key] = atlas;
        }

        return atlas;
    }

    /// <inheritdoc />
    public SDFFontAtlas GetOrCreateFromEmbeddedResource(
        string name,
        string pngResourceName,
        string jsonResourceName,
        Assembly assembly,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(assembly);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var cached))
                return cached;
        }

        var assemblyName = assembly.GetName().Name;
        var fullPngName = $"{assemblyName}.{pngResourceName}";
        var fullJsonName = $"{assemblyName}.{jsonResourceName}";

        // Load texture
        using var pngStream =
            assembly.GetManifestResourceStream(fullPngName)
            ?? throw new FileNotFoundException($"Embedded resource '{fullPngName}' not found.");
        var textureRef = textureRepository.GetOrCreateFromStream(
            $"FontAtlas:{name}",
            pngStream,
            generateMipmaps: false,
            debugName: $"SDFFont_Atlas_{name}"
        );
        uint textureIndex = textureRef;

        // Create sampler
        var samplerRef = samplerRepository.GetOrCreate(SamplerStateDesc.LinearClampNoMipmap);
        uint samplerIndex = samplerRef;

        // Load descriptor
        using var jsonStream =
            assembly.GetManifestResourceStream(fullJsonName)
            ?? throw new FileNotFoundException($"Embedded resource '{fullJsonName}' not found.");
        var descriptor = SDFFontAtlasLoader.LoadFromStream(jsonStream);
        var atlas = new SDFFontAtlas(textureIndex, samplerIndex, descriptor);

        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var existing))
                return existing;
            _cache[name] = atlas;
        }

        return atlas;
    }

    /// <inheritdoc />
    public SDFFontAtlas GetOrCreateFromFiles(
        string name,
        string pngFilePath,
        string jsonFilePath,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(pngFilePath);
        ArgumentException.ThrowIfNullOrEmpty(jsonFilePath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var cached))
                return cached;
        }

        if (!File.Exists(pngFilePath))
            throw new FileNotFoundException(
                $"Font atlas PNG file not found: '{pngFilePath}'",
                pngFilePath
            );
        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException(
                $"Font atlas JSON file not found: '{jsonFilePath}'",
                jsonFilePath
            );

        // Load texture
        var textureRef = textureRepository.GetOrCreateFromFile(
            pngFilePath,
            generateMipmaps: false,
            debugName: $"SDFFont_Atlas_{name}"
        );
        uint textureIndex = textureRef;

        // Create sampler
        var samplerRef = samplerRepository.GetOrCreate(SamplerStateDesc.LinearClampNoMipmap);
        uint samplerIndex = samplerRef;

        // Load descriptor
        using var jsonStream = File.OpenRead(jsonFilePath);
        var descriptor = SDFFontAtlasLoader.LoadFromStream(jsonStream);
        var atlas = new SDFFontAtlas(textureIndex, samplerIndex, descriptor);

        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var existing))
                return existing;
            _cache[name] = atlas;
        }

        return atlas;
    }

    /// <inheritdoc />
    public bool TryGet(string name, out SDFFontAtlas? atlas)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                atlas = cached;
                return true;
            }
        }
        atlas = null;
        return false;
    }

    /// <inheritdoc />
    public bool Remove(string name)
    {
        lock (_lock)
        {
            return _cache.Remove(name);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// Maps a <see cref="BuildinFontAtlas"/> enum value to the corresponding embedded resource names.
    /// </summary>
    private static (string PngResource, string JsonResource) GetBuiltInResourceNames(
        BuildinFontAtlas atlasType
    )
    {
        return atlasType switch
        {
            BuildinFontAtlas.GoogleSansRegular => (
                "Assets.google-sans-regular.png",
                "Assets.google-sans-regular.json"
            ),
            BuildinFontAtlas.RobotoSlabRegular => (
                "Assets.robotoslab-sans-regular.png",
                "Assets.robotoslab-sans-regular.json"
            ),
            BuildinFontAtlas.MichromaRegular => (
                "Assets.michroma-regular.png",
                "Assets.michroma-regular.json"
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(atlasType),
                $"Unsupported atlas type: {atlasType}"
            ),
        };
    }
}
