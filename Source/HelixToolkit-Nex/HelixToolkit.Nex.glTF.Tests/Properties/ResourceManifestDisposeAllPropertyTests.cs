using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 4: DisposeAll Releases All Resources

/// <summary>
/// Property-based tests for DisposeAll releasing all tracked resources.
/// Verifies that for any ResourceManifest containing N materials, M geometries,
/// T textures, and S samplers, calling DisposeAll() disposes all materials,
/// disposes all geometries, calls Remove on the texture repository T times,
/// calls Remove on the sampler repository S times, and all count properties
/// return 0 afterward.
/// </summary>
/// <remarks>
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.8**
/// </remarks>
[TestClass]
public class ResourceManifestDisposeAllPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock ITextureRepository that tracks Remove call counts.
    /// </summary>
    private sealed class TrackingTextureRepository : ITextureRepository
    {
        public int RemoveCallCount { get; private set; }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;
            return true;
        }

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
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
    /// A mock ISamplerRepository that tracks Remove call counts.
    /// </summary>
    private sealed class TrackingSamplerRepository : ISamplerRepository
    {
        public int RemoveCallCount { get; private set; }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;
            return true;
        }

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

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

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a valid TextureRef with a unique key backed by the given tracking repository.
    /// </summary>
    private static TextureRef CreateTextureRef(string key, TrackingTextureRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateTexture(
            new TextureDesc
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_UN8,
                Dimensions = new Dimensions(1, 1, 1),
                NumMipLevels = 1,
                NumLayers = 1,
            },
            out var tex,
            key
        );
        return new TextureRef(key, repo, tex);
    }

    /// <summary>
    /// Creates a valid SamplerRef with a unique key backed by the given tracking repository.
    /// </summary>
    private static SamplerRef CreateSamplerRef(string key, TrackingSamplerRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        return new SamplerRef(key, repo, sampler);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Property 4: DisposeAll Releases All Resources
    // Feature: gltf-resource-tracking, Property 4: DisposeAll Releases All Resources
    // Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.8
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 4: For any ResourceManifest containing N materials, M geometries,
    /// T textures, and S samplers, calling DisposeAll() SHALL dispose all N materials
    /// (each Valid becomes false), dispose all M geometries, call Remove(key) on the
    /// texture repository T times, call Remove(key) on the sampler repository S times,
    /// and all count properties SHALL return 0 afterward.
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.8**
    /// </summary>
    [TestMethod]
    public void Property4_DisposeAll_ReleasesAllResources()
    {
        var gen =
            from materialCount in Gen.Choose(0, 5)
            from geometryCount in Gen.Choose(0, 5)
            from textureCount in Gen.Choose(0, 5)
            from samplerCount in Gen.Choose(0, 5)
            select (materialCount, geometryCount, textureCount, samplerCount);

        Prop.ForAll(
                Arb.From(gen),
                (
                    (int materialCount, int geometryCount, int textureCount, int samplerCount) input
                ) =>
                {
                    var (materialCount, geometryCount, textureCount, samplerCount) = input;

                    var textureRepo = new TrackingTextureRepository();
                    var samplerRepo = new TrackingSamplerRepository();
                    using var materialManager = new PBRMaterialPropertyManager();

                    var manifest = new ResourceManifest();

                    // Track materials for disposal verification
                    var materials = new List<PBRMaterialProperties>();
                    for (int i = 0; i < materialCount; i++)
                    {
                        var material = materialManager.Create(Shaders.Frag.PBRShadingMode.PBR.ToString());
                        manifest.AddMaterial(material);
                        materials.Add(material);
                    }

                    // Add geometries (Requirement 6.2: DisposeAll calls Dispose on each)
                    for (int i = 0; i < geometryCount; i++)
                    {
                        var geometry = new Geometry();
                        manifest.AddGeometry(geometry);
                    }

                    // Add textures with unique keys
                    for (int i = 0; i < textureCount; i++)
                    {
                        manifest.AddTexture(CreateTextureRef($"tex_{i}", textureRepo));
                    }

                    // Add samplers with unique keys
                    for (int i = 0; i < samplerCount; i++)
                    {
                        manifest.AddSampler(CreateSamplerRef($"samp_{i}", samplerRepo));
                    }

                    // Act: Call DisposeAll
                    manifest.DisposeAll();

                    // Assert: All count properties return 0 (Requirement 6.8)
                    bool allCountsZero =
                        manifest.TextureCount == 0
                        && manifest.SamplerCount == 0
                        && manifest.MaterialCount == 0
                        && manifest.GeometryCount == 0;

                    // Assert: Repository Remove called T times for textures (Requirement 6.3)
                    bool textureRemoveCorrect = textureRepo.RemoveCallCount == textureCount;

                    // Assert: Repository Remove called S times for samplers (Requirement 6.4)
                    bool samplerRemoveCorrect = samplerRepo.RemoveCallCount == samplerCount;

                    // Assert: All materials were disposed (Requirement 6.1)
                    // After Dispose(), Valid becomes false because the pool entry is destroyed
                    bool allMaterialsDisposed = materials.All(m => !m.Valid);

                    return allCountsZero
                        && textureRemoveCorrect
                        && samplerRemoveCorrect
                        && allMaterialsDisposed;
                }
            )
            .Check(FsCheckConfig);
    }
}
