using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 5: Disposal Ordering

/// <summary>
/// Property-based tests for disposal ordering of ResourceManifest.
/// Verifies that for any ResourceManifest containing both materials/geometries and
/// textures/samplers, DisposeAll() SHALL dispose all PBRMaterialProperties instances
/// and all Geometry instances before calling Remove on any ITextureRepository or
/// ISamplerRepository entry.
/// </summary>
/// <remarks>
/// **Validates: Requirements 6.6, 6.7**
/// </remarks>
[TestClass]
public class ResourceManifestDisposalOrderingPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A Geometry subclass that tracks whether Dispose has been called.
    /// Uses the protected virtual Dispose(bool) override to detect disposal.
    /// </summary>
    private sealed class TrackableGeometry : Geometry
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A mock ITextureRepository that verifies all materials and geometries are already
    /// disposed at the time Remove is called. Records whether the ordering constraint held.
    /// </summary>
    private sealed class OrderVerifyingTextureRepository : ITextureRepository
    {
        private readonly List<PBRMaterialProperties> _materials;
        private readonly List<TrackableGeometry> _geometries;

        public int RemoveCallCount { get; private set; }

        /// <summary>
        /// True if all materials were disposed (Valid == false) at the time of every Remove call.
        /// </summary>
        public bool AllMaterialsDisposedBeforeRemove { get; private set; } = true;

        /// <summary>
        /// True if all geometries were disposed at the time of every Remove call.
        /// </summary>
        public bool AllGeometriesDisposedBeforeRemove { get; private set; } = true;

        public OrderVerifyingTextureRepository(
            List<PBRMaterialProperties> materials,
            List<TrackableGeometry> geometries
        )
        {
            _materials = materials;
            _geometries = geometries;
        }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;

            // At the time of this Remove call, all materials should already be disposed
            if (_materials.Any(m => m.Valid))
            {
                AllMaterialsDisposedBeforeRemove = false;
            }

            // At the time of this Remove call, all geometries should already be disposed
            if (_geometries.Any(g => !g.IsDisposed))
            {
                AllGeometriesDisposedBeforeRemove = false;
            }

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
    /// A mock ISamplerRepository that verifies all materials and geometries are already
    /// disposed at the time Remove is called. Records whether the ordering constraint held.
    /// </summary>
    private sealed class OrderVerifyingSamplerRepository : ISamplerRepository
    {
        private readonly List<PBRMaterialProperties> _materials;
        private readonly List<TrackableGeometry> _geometries;

        public int RemoveCallCount { get; private set; }

        /// <summary>
        /// True if all materials were disposed (Valid == false) at the time of every Remove call.
        /// </summary>
        public bool AllMaterialsDisposedBeforeRemove { get; private set; } = true;

        /// <summary>
        /// True if all geometries were disposed at the time of every Remove call.
        /// </summary>
        public bool AllGeometriesDisposedBeforeRemove { get; private set; } = true;

        public OrderVerifyingSamplerRepository(
            List<PBRMaterialProperties> materials,
            List<TrackableGeometry> geometries
        )
        {
            _materials = materials;
            _geometries = geometries;
        }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;

            // At the time of this Remove call, all materials should already be disposed
            if (_materials.Any(m => m.Valid))
            {
                AllMaterialsDisposedBeforeRemove = false;
            }

            // At the time of this Remove call, all geometries should already be disposed
            if (_geometries.Any(g => !g.IsDisposed))
            {
                AllGeometriesDisposedBeforeRemove = false;
            }

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
    /// Creates a valid TextureRef with a unique key backed by the given repository.
    /// </summary>
    private static TextureRef CreateTextureRef(string key, OrderVerifyingTextureRepository repo)
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
    /// Creates a valid SamplerRef with a unique key backed by the given repository.
    /// </summary>
    private static SamplerRef CreateSamplerRef(string key, OrderVerifyingSamplerRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        return new SamplerRef(key, repo, sampler);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Property 5: Disposal Ordering
    // Feature: gltf-resource-tracking, Property 5: Disposal Ordering
    // Validates: Requirements 6.6, 6.7
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 5: For any ResourceManifest containing both materials/geometries and
    /// textures/samplers, DisposeAll() SHALL dispose all PBRMaterialProperties instances
    /// and all Geometry instances before calling Remove on any ITextureRepository or
    /// ISamplerRepository entry.
    ///
    /// Strategy: Mock repositories check at the time of each Remove call that all
    /// materials have Valid == false and all geometries have IsDisposed == true.
    /// This directly verifies the ordering constraint.
    ///
    /// **Validates: Requirements 6.6, 6.7**
    /// </summary>
    [TestMethod]
    public void Property5_DisposeAll_MaterialsAndGeometriesDisposedBeforeTextureAndSamplerRemoval()
    {
        // Generate manifests that always have at least 1 material OR geometry
        // AND at least 1 texture OR sampler to ensure the ordering constraint is meaningful.
        var gen =
            from materialCount in Gen.Choose(1, 5)
            from geometryCount in Gen.Choose(1, 5)
            from textureCount in Gen.Choose(1, 5)
            from samplerCount in Gen.Choose(1, 5)
            select (materialCount, geometryCount, textureCount, samplerCount);

        Prop.ForAll(
                Arb.From(gen),
                (
                    (int materialCount, int geometryCount, int textureCount, int samplerCount) input
                ) =>
                {
                    var (materialCount, geometryCount, textureCount, samplerCount) = input;

                    using var materialManager = new PBRMaterialPropertyManager();

                    // Create materials and geometries first so we can pass them to repositories
                    var materials = new List<PBRMaterialProperties>();
                    for (int i = 0; i < materialCount; i++)
                    {
                        var material = materialManager.Create(Shaders.Frag.PBRShadingMode.PBR);
                        materials.Add(material);
                    }

                    var geometries = new List<TrackableGeometry>();
                    for (int i = 0; i < geometryCount; i++)
                    {
                        geometries.Add(new TrackableGeometry());
                    }

                    // Create repositories that will verify ordering at Remove time
                    var textureRepo = new OrderVerifyingTextureRepository(materials, geometries);
                    var samplerRepo = new OrderVerifyingSamplerRepository(materials, geometries);

                    var manifest = new ResourceManifest();

                    // Add materials to manifest
                    foreach (var material in materials)
                    {
                        manifest.AddMaterial(material);
                    }

                    // Add geometries to manifest
                    foreach (var geometry in geometries)
                    {
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

                    // Assert: Ordering constraint held — all materials were disposed
                    // before any texture Remove call (Requirement 6.6)
                    bool materialsBeforeTextures = textureRepo.AllMaterialsDisposedBeforeRemove;

                    // Assert: Ordering constraint held — all materials were disposed
                    // before any sampler Remove call (Requirement 6.6)
                    bool materialsBeforeSamplers = samplerRepo.AllMaterialsDisposedBeforeRemove;

                    // Assert: Ordering constraint held — all geometries were disposed
                    // before any texture Remove call (Requirement 6.7)
                    bool geometriesBeforeTextures = textureRepo.AllGeometriesDisposedBeforeRemove;

                    // Assert: Ordering constraint held — all geometries were disposed
                    // before any sampler Remove call (Requirement 6.7)
                    bool geometriesBeforeSamplers = samplerRepo.AllGeometriesDisposedBeforeRemove;

                    // Assert: Remove was actually called (sanity check)
                    bool textureRemoveCalled = textureRepo.RemoveCallCount == textureCount;
                    bool samplerRemoveCalled = samplerRepo.RemoveCallCount == samplerCount;

                    return materialsBeforeTextures
                        && materialsBeforeSamplers
                        && geometriesBeforeTextures
                        && geometriesBeforeSamplers
                        && textureRemoveCalled
                        && samplerRemoveCalled;
                }
            )
            .Check(FsCheckConfig);
    }
}
