using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders.Frag;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for the ResourceManifest class.
/// Validates: Requirements 1.5, 1.6, 2.3, 2.4, 3.2, 3.3, 5.3, 6.5, 6.8, 9.5
/// </summary>
[TestClass]
public class ResourceManifestTests
{
    /// <summary>
    /// A stub ITextureRepository that tracks Remove calls for verification.
    /// </summary>
    private sealed class StubTextureRepository : ITextureRepository
    {
        public int Count => 0;
        public List<string> RemovedKeys { get; } = [];

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
            NexImage image,
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
            NexImage image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key)
        {
            RemovedKeys.Add(key);
            return true;
        }

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
    /// A stub ISamplerRepository that tracks Remove calls for verification.
    /// </summary>
    private sealed class StubSamplerRepository : ISamplerRepository
    {
        public int Count => 0;
        public List<string> RemovedKeys { get; } = [];

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

        public bool Remove(string key)
        {
            RemovedKeys.Add(key);
            return true;
        }

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

    private StubTextureRepository _textureRepo = null!;
    private StubSamplerRepository _samplerRepo = null!;
    private PBRMaterialPropertyManager _materialManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _textureRepo = new StubTextureRepository();
        _samplerRepo = new StubSamplerRepository();
        _materialManager = new PBRMaterialPropertyManager();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _materialManager?.Dispose();
        _textureRepo?.Dispose();
        _samplerRepo?.Dispose();
    }

    private TextureRef CreateTextureRef(string key)
    {
        return new TextureRef(key, _textureRepo, new TextureResource());
    }

    private SamplerRef CreateSamplerRef(string key)
    {
        return new SamplerRef(key, _samplerRepo, new SamplerResource());
    }

    private PBRMaterialProperties CreateMaterial()
    {
        return _materialManager.Create(PBRShadingMode.PBR);
    }

    // =========================================================================
    // Empty manifest — Validates: Requirement 9.5
    // =========================================================================

    [TestMethod]
    public void EmptyManifest_HasZeroCounts_ForAllResourceTypes()
    {
        var manifest = new ResourceManifest();

        Assert.AreEqual(0, manifest.TextureCount);
        Assert.AreEqual(0, manifest.SamplerCount);
        Assert.AreEqual(0, manifest.MaterialCount);
        Assert.AreEqual(0, manifest.GeometryCount);
        Assert.AreEqual(0, manifest.Textures.Count);
        Assert.AreEqual(0, manifest.Samplers.Count);
        Assert.AreEqual(0, manifest.Materials.Count);
        Assert.AreEqual(0, manifest.Geometries.Count);
    }

    // =========================================================================
    // AddTexture — Validates: Requirements 1.6, 2.3, 2.4
    // =========================================================================

    [TestMethod]
    public void AddTexture_ValidTextureRef_IncrementsTextureCount_AndAppearsInCollection()
    {
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("texture_key_1");

        manifest.AddTexture(textureRef);

        Assert.AreEqual(1, manifest.TextureCount);
        Assert.AreEqual(1, manifest.Textures.Count);
        Assert.AreSame(textureRef, manifest.Textures[0]);
    }

    [TestMethod]
    public void AddTexture_TextureRefNull_DoesNotAddToCollection()
    {
        var manifest = new ResourceManifest();

        manifest.AddTexture(TextureRef.Null);

        Assert.AreEqual(0, manifest.TextureCount);
        Assert.AreEqual(0, manifest.Textures.Count);
    }

    [TestMethod]
    public void AddTexture_DuplicateKey_Deduplicates_CountStaysOne()
    {
        var manifest = new ResourceManifest();
        var textureRef1 = CreateTextureRef("same_key");
        var textureRef2 = CreateTextureRef("same_key");

        manifest.AddTexture(textureRef1);
        manifest.AddTexture(textureRef2);

        Assert.AreEqual(1, manifest.TextureCount);
        Assert.AreEqual(1, manifest.Textures.Count);
        Assert.AreSame(textureRef1, manifest.Textures[0]);
    }

    // =========================================================================
    // AddSampler — Validates: Requirements 3.2, 3.3
    // =========================================================================

    [TestMethod]
    public void AddSampler_ValidSamplerRef_IncrementsSamplerCount()
    {
        var manifest = new ResourceManifest();
        var samplerRef = CreateSamplerRef("sampler_key_1");

        manifest.AddSampler(samplerRef);

        Assert.AreEqual(1, manifest.SamplerCount);
        Assert.AreEqual(1, manifest.Samplers.Count);
        Assert.AreSame(samplerRef, manifest.Samplers[0]);
    }

    [TestMethod]
    public void AddSampler_SamplerRefNull_DoesNotAddToCollection()
    {
        var manifest = new ResourceManifest();

        manifest.AddSampler(SamplerRef.Null);

        Assert.AreEqual(0, manifest.SamplerCount);
        Assert.AreEqual(0, manifest.Samplers.Count);
    }

    [TestMethod]
    public void AddSampler_SameReference_Deduplicates()
    {
        var manifest = new ResourceManifest();
        var samplerRef = CreateSamplerRef("sampler_key");

        manifest.AddSampler(samplerRef);
        manifest.AddSampler(samplerRef);

        Assert.AreEqual(1, manifest.SamplerCount);
        Assert.AreEqual(1, manifest.Samplers.Count);
    }

    // =========================================================================
    // AddMaterial — Validates: Requirement 1.5 (no deduplication)
    // =========================================================================

    [TestMethod]
    public void AddMaterial_IncrementsMaterialCount_NoDeduplication()
    {
        var manifest = new ResourceManifest();
        var material = CreateMaterial();

        manifest.AddMaterial(material);

        Assert.AreEqual(1, manifest.MaterialCount);
        Assert.AreEqual(1, manifest.Materials.Count);
        Assert.AreSame(material, manifest.Materials[0]);

        material.Dispose();
    }

    // =========================================================================
    // AddGeometry — Validates: Requirements 5.3
    // =========================================================================

    [TestMethod]
    public void AddGeometry_ValidGeometry_IncrementsGeometryCount()
    {
        var manifest = new ResourceManifest();
        var geometry = new Geometry();

        manifest.AddGeometry(geometry);

        Assert.AreEqual(1, manifest.GeometryCount);
        Assert.AreEqual(1, manifest.Geometries.Count);
        Assert.AreSame(geometry, manifest.Geometries[0]);
    }

    [TestMethod]
    public void AddGeometry_Null_DoesNotAddToCollection()
    {
        var manifest = new ResourceManifest();

        manifest.AddGeometry(null);

        Assert.AreEqual(0, manifest.GeometryCount);
        Assert.AreEqual(0, manifest.Geometries.Count);
    }

    // =========================================================================
    // DisposeAll — Validates: Requirements 6.5, 6.8
    // =========================================================================

    [TestMethod]
    public void DisposeAll_SetsAllCountsToZero()
    {
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_dispose");
        var samplerRef = CreateSamplerRef("samp_dispose");
        var material = CreateMaterial();
        var geometry = new Geometry();

        manifest.AddTexture(textureRef);
        manifest.AddSampler(samplerRef);
        manifest.AddMaterial(material);
        manifest.AddGeometry(geometry);

        // Verify resources were added
        Assert.AreEqual(1, manifest.TextureCount);
        Assert.AreEqual(1, manifest.SamplerCount);
        Assert.AreEqual(1, manifest.MaterialCount);
        Assert.AreEqual(1, manifest.GeometryCount);

        manifest.DisposeAll();

        Assert.AreEqual(0, manifest.TextureCount);
        Assert.AreEqual(0, manifest.SamplerCount);
        Assert.AreEqual(0, manifest.MaterialCount);
        Assert.AreEqual(0, manifest.GeometryCount);
    }

    [TestMethod]
    public void DisposeAll_CalledTwice_DoesNotThrow()
    {
        var manifest = new ResourceManifest();
        var textureRef = CreateTextureRef("tex_idempotent");
        var samplerRef = CreateSamplerRef("samp_idempotent");
        var material = CreateMaterial();
        var geometry = new Geometry();

        manifest.AddTexture(textureRef);
        manifest.AddSampler(samplerRef);
        manifest.AddMaterial(material);
        manifest.AddGeometry(geometry);

        manifest.DisposeAll();

        // Second call should not throw
        manifest.DisposeAll();

        // Verify Remove was only called once per resource
        Assert.AreEqual(1, _textureRepo.RemovedKeys.Count);
        Assert.AreEqual(1, _samplerRepo.RemovedKeys.Count);
    }

    // =========================================================================
    // Empty sentinel — Validates: Requirement 1.5
    // =========================================================================

    [TestMethod]
    public void EmptySentinel_AddMethods_AreNoOps()
    {
        var empty = ResourceManifest.Empty;
        var textureRef = CreateTextureRef("tex_empty");
        var samplerRef = CreateSamplerRef("samp_empty");
        var material = CreateMaterial();
        var geometry = new Geometry();

        // These should be no-ops on the Empty sentinel
        empty.AddTexture(textureRef);
        empty.AddSampler(samplerRef);
        empty.AddMaterial(material);
        empty.AddGeometry(geometry);

        Assert.AreEqual(0, empty.TextureCount);
        Assert.AreEqual(0, empty.SamplerCount);
        Assert.AreEqual(0, empty.MaterialCount);
        Assert.AreEqual(0, empty.GeometryCount);

        material.Dispose();
    }
}
