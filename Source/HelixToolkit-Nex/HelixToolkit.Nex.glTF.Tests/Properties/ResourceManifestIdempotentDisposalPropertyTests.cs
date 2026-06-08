using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 6: Idempotent Disposal

/// <summary>
/// Property-based tests for idempotent disposal of ResourceManifest.
/// Verifies that calling DisposeAll() K times (K >= 1) has the same observable effect
/// as calling it exactly once — no double-dispose exceptions, no additional repository
/// Remove calls, and counts remain zero after the first call.
/// </summary>
/// <remarks>
/// **Validates: Requirements 6.5, 7.4**
/// </remarks>
[TestClass]
public class ResourceManifestIdempotentDisposalPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock ITextureRepository that tracks Remove call counts and never throws.
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
    /// A mock ISamplerRepository that tracks Remove call counts and never throws.
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
    // Property 6: Idempotent Disposal
    // Feature: gltf-resource-tracking, Property 6: Idempotent Disposal
    // Validates: Requirements 6.5, 7.4
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property6_DisposeAll_CalledMultipleTimes_IsIdempotent()
    {
        // Generate manifests with random resource counts, call DisposeAll K times,
        // assert no exceptions and counts remain zero after first call.
        var gen =
            from textureCount in Gen.Choose(0, 5)
            from samplerCount in Gen.Choose(0, 5)
            from materialCount in Gen.Choose(0, 5)
            from geometryCount in Gen.Choose(0, 5)
            from disposeCallCount in Gen.Choose(1, 5)
            select (textureCount, samplerCount, materialCount, geometryCount, disposeCallCount);

        Prop.ForAll(
                Arb.From(gen),
                (
                    (
                        int textureCount,
                        int samplerCount,
                        int materialCount,
                        int geometryCount,
                        int disposeCallCount
                    ) input
                ) =>
                {
                    var (
                        textureCount,
                        samplerCount,
                        materialCount,
                        geometryCount,
                        disposeCallCount
                    ) = input;

                    var textureRepo = new TrackingTextureRepository();
                    var samplerRepo = new TrackingSamplerRepository();
                    using var materialManager = new PBRMaterialPropertyManager();

                    var manifest = new ResourceManifest();

                    // Add textures with unique keys
                    for (int i = 0; i < textureCount; i++)
                    {
                        var textureRef = CreateTextureRef($"tex_{i}", textureRepo);
                        manifest.AddTexture(textureRef);
                    }

                    // Add samplers with unique keys
                    for (int i = 0; i < samplerCount; i++)
                    {
                        var samplerRef = CreateSamplerRef($"samp_{i}", samplerRepo);
                        manifest.AddSampler(samplerRef);
                    }

                    // Add materials
                    for (int i = 0; i < materialCount; i++)
                    {
                        var material = materialManager.Create(Shaders.Frag.PBRShadingMode.PBR);
                        manifest.AddMaterial(material);
                    }

                    // Add geometries
                    for (int i = 0; i < geometryCount; i++)
                    {
                        var geometry = new Geometry();
                        manifest.AddGeometry(geometry);
                    }

                    // Call DisposeAll K times — should not throw
                    for (int k = 0; k < disposeCallCount; k++)
                    {
                        manifest.DisposeAll();
                    }

                    // After disposal, all counts must be zero
                    bool countsZero =
                        manifest.TextureCount == 0
                        && manifest.SamplerCount == 0
                        && manifest.MaterialCount == 0
                        && manifest.GeometryCount == 0;

                    // Repository Remove should have been called exactly once per resource
                    // (not K times per resource)
                    bool textureRemoveCorrect = textureRepo.RemoveCallCount == textureCount;
                    bool samplerRemoveCorrect = samplerRepo.RemoveCallCount == samplerCount;

                    return countsZero && textureRemoveCorrect && samplerRemoveCorrect;
                }
            )
            .Check(FsCheckConfig);
    }
}
