using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using GltfSampler = glTFLoader.Schema.Sampler;
using GltfTexture = glTFLoader.Schema.Texture;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests verifying that the glTF importer resolves a texture's sampler and assigns it to the
/// engine material's single <see cref="HelixToolkit.Nex.Material.PBRMaterialProperties.Sampler"/>
/// slot. This is a regression guard for the bug where <c>TextureLoader.LoadSampler</c> was never
/// invoked by <c>MaterialConverter</c>, so glTF wrap/filter settings were silently dropped.
/// </summary>
[TestClass]
public class MaterialConverterSamplerTests
{
    /// <summary>
    /// An ISamplerRepository that records every (key, desc) passed to GetOrCreate and returns a
    /// real, per-key cached SamplerRef so the converter can assign it to the material.
    /// </summary>
    private sealed class CapturingSamplerRepository : ISamplerRepository
    {
        private readonly Dictionary<string, SamplerRef> _cache = [];

        public List<(string Key, SamplerStateDesc Desc)> Created { get; } = [];

        public int Count => _cache.Count;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc)
        {
            Created.Add((key, desc));
            if (!_cache.TryGetValue(key, out var existing))
            {
                existing = new SamplerRef(key, this, SamplerResource.Null);
                _cache[key] = existing;
            }
            return existing;
        }

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
    /// An ITextureRepository that always returns TextureRef.Null. Sampler resolution does not depend
    /// on the image loading, so a null-returning texture repo keeps these tests focused on samplers.
    /// </summary>
    private sealed class NullTextureRepository : ITextureRepository
    {
        public int Count => 0;

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

        public bool Remove(string key) => false;

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

    private StubMaterialPropertyManager _materialManager = null!;
    private NullTextureRepository _textureRepo = null!;
    private CapturingSamplerRepository _samplerRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _materialManager = new StubMaterialPropertyManager();
        _textureRepo = new NullTextureRepository();
        _samplerRepo = new CapturingSamplerRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _materialManager?.Dispose();
        _textureRepo?.Dispose();
        _samplerRepo?.Dispose();
    }

    private MaterialConverter CreateConverter(Gltf model, List<ImportDiagnostic> diagnostics)
    {
        var manifest = new ResourceManifest();
        var textureLoader = new TextureLoader(
            _textureRepo,
            _samplerRepo,
            "C:\\test",
            model,
            [],
            diagnostics,
            manifest,
            Guid.NewGuid().ToString("D")
        );
        return new MaterialConverter(_materialManager, textureLoader, diagnostics, manifest);
    }

    /// <summary>
    /// A glTF sampler describing CLAMP_TO_EDGE wrapping and NEAREST (non-mipmapped) filtering — the
    /// opposite of the engine defaults — so the test fails if the sampler is dropped.
    /// </summary>
    private static GltfSampler ClampNearestSampler(string name) =>
        new()
        {
            Name = name,
            MagFilter = GltfSampler.MagFilterEnum.NEAREST,
            MinFilter = GltfSampler.MinFilterEnum.NEAREST,
            WrapS = GltfSampler.WrapSEnum.CLAMP_TO_EDGE,
            WrapT = GltfSampler.WrapTEnum.CLAMP_TO_EDGE,
        };

    [TestMethod]
    public void ConvertMaterial_BaseColorTextureSampler_IsAppliedToMaterialSampler()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "Mat",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        BaseColorTexture = new TextureInfo { Index = 0 },
                    },
                },
            ],
            Textures = [new GltfTexture { Source = 0, Sampler = 0 }],
            Samplers = [ClampNearestSampler("ClampNearest")],
        };

        var converter = CreateConverter(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        // The material's sampler must have been assigned (not the default null sampler).
        Assert.AreNotEqual(
            SamplerRef.Null,
            material.Sampler,
            "Material.Sampler should be assigned from the base color texture's glTF sampler."
        );

        // Exactly one sampler should have been created, and its desc must reflect the glTF sampler.
        Assert.AreEqual(1, _samplerRepo.Created.Count);
        var desc = _samplerRepo.Created[0].Desc;
        Assert.AreEqual(SamplerWrap.Clamp, desc.WrapU, "WrapU should map from CLAMP_TO_EDGE.");
        Assert.AreEqual(SamplerWrap.Clamp, desc.WrapV, "WrapV should map from CLAMP_TO_EDGE.");
        Assert.AreEqual(SamplerFilter.Nearest, desc.MagFilter, "MagFilter should map from NEAREST.");
        Assert.AreEqual(SamplerFilter.Nearest, desc.MinFilter, "MinFilter should map from NEAREST.");
        Assert.AreEqual(SamplerMip.Disabled, desc.MipMap, "MipMap should be disabled for NEAREST.");

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_TextureWithoutSampler_LeavesDefaultSampler()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "Mat",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        BaseColorTexture = new TextureInfo { Index = 0 },
                    },
                },
            ],
            // Texture references no sampler → glTF default sampler, nothing to assign.
            Textures = [new GltfTexture { Source = 0, Sampler = null }],
        };

        var converter = CreateConverter(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            SamplerRef.Null,
            material.Sampler,
            "Material.Sampler should stay at the default when no glTF sampler is referenced."
        );
        Assert.AreEqual(
            0,
            _samplerRepo.Created.Count,
            "No sampler should be created when the texture references no sampler."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_FallsBackToOtherMapSampler_WhenBaseColorHasNoSampler()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "Mat",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        // Base color texture has no sampler.
                        BaseColorTexture = new TextureInfo { Index = 0 },
                    },
                    // Normal texture references a sampler — should be used as the fallback.
                    NormalTexture = new MaterialNormalTextureInfo { Index = 1 },
                },
            ],
            Textures =
            [
                new GltfTexture { Source = 0, Sampler = null },
                new GltfTexture { Source = 1, Sampler = 0 },
            ],
            Samplers = [ClampNearestSampler("NormalSampler")],
        };

        var converter = CreateConverter(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreNotEqual(
            SamplerRef.Null,
            material.Sampler,
            "Material.Sampler should fall back to the normal map's glTF sampler."
        );
        Assert.AreEqual(1, _samplerRepo.Created.Count);
        Assert.AreEqual("NormalSampler", _samplerRepo.Created[0].Desc.DebugName);

        material.Dispose();
    }

    [TestMethod]
    public async Task ConvertMaterialAsync_BaseColorTextureSampler_IsAppliedToMaterialSampler()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "Mat",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        BaseColorTexture = new TextureInfo { Index = 0 },
                    },
                },
            ],
            Textures = [new GltfTexture { Source = 0, Sampler = 0 }],
            Samplers = [ClampNearestSampler("ClampNearest")],
        };

        var converter = CreateConverter(model, diagnostics);
        var material = await converter.ConvertMaterialAsync(model, 0);

        Assert.AreNotEqual(
            SamplerRef.Null,
            material.Sampler,
            "Async path should also assign Material.Sampler from the glTF sampler."
        );
        Assert.AreEqual(1, _samplerRepo.Created.Count);
        Assert.AreEqual(SamplerWrap.Clamp, _samplerRepo.Created[0].Desc.WrapU);

        material.Dispose();
    }
}
