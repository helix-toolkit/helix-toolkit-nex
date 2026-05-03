using System.Reflection;

namespace HelixToolkit.Nex.Rendering.SDF;

/// <summary>
/// Interface for a repository that caches <see cref="SDFFontAtlas"/> instances by name.
/// Provides methods to load font atlases from embedded resources or external files.
/// </summary>
public interface IFontAtlasRepository : IDisposable
{
    /// <summary>
    /// Gets the number of cached font atlases.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets or creates a font atlas from the built-in embedded resource.
    /// Uses "BuiltIn:{atlasType}" as the cache key.
    /// </summary>
    /// <param name="atlasType">The built-in font atlas type to load.</param>
    /// <param name="textureRepository">The texture repository for loading the atlas PNG.</param>
    /// <param name="samplerRepository">The sampler repository for creating the atlas sampler.</param>
    /// <returns>A cached <see cref="SDFFontAtlas"/> instance.</returns>
    SDFFontAtlas GetOrCreateBuiltIn(
        BuildinFontAtlas atlasType,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    );

    /// <summary>
    /// Gets or creates a font atlas from an embedded resource in the specified assembly.
    /// </summary>
    /// <param name="name">A unique name for this font atlas in the cache.</param>
    /// <param name="pngResourceName">The embedded resource name for the atlas PNG (e.g., "Assets.myfont.png").</param>
    /// <param name="jsonResourceName">The embedded resource name for the atlas JSON (e.g., "Assets.myfont.json").</param>
    /// <param name="assembly">The assembly containing the embedded resources.</param>
    /// <param name="textureRepository">The texture repository for loading the atlas PNG.</param>
    /// <param name="samplerRepository">The sampler repository for creating the atlas sampler.</param>
    /// <returns>A cached <see cref="SDFFontAtlas"/> instance.</returns>
    SDFFontAtlas GetOrCreateFromEmbeddedResource(
        string name,
        string pngResourceName,
        string jsonResourceName,
        Assembly assembly,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    );

    /// <summary>
    /// Gets or creates a font atlas from external file paths.
    /// </summary>
    /// <param name="name">A unique name for this font atlas in the cache.</param>
    /// <param name="pngFilePath">Path to the atlas PNG texture file.</param>
    /// <param name="jsonFilePath">Path to the msdf-atlas-gen JSON descriptor file.</param>
    /// <param name="textureRepository">The texture repository for loading the atlas PNG.</param>
    /// <param name="samplerRepository">The sampler repository for creating the atlas sampler.</param>
    /// <returns>A cached <see cref="SDFFontAtlas"/> instance.</returns>
    SDFFontAtlas GetOrCreateFromFiles(
        string name,
        string pngFilePath,
        string jsonFilePath,
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository
    );

    /// <summary>
    /// Attempts to retrieve a cached font atlas by name.
    /// </summary>
    /// <param name="name">The cache key.</param>
    /// <param name="atlas">The cached atlas if found.</param>
    /// <returns><c>true</c> if found; otherwise <c>false</c>.</returns>
    bool TryGet(string name, out SDFFontAtlas? atlas);

    /// <summary>
    /// Removes a font atlas from the cache.
    /// </summary>
    /// <param name="name">The cache key.</param>
    /// <returns><c>true</c> if found and removed; otherwise <c>false</c>.</returns>
    bool Remove(string name);

    /// <summary>
    /// Clears all cached font atlases.
    /// </summary>
    void Clear();
}
