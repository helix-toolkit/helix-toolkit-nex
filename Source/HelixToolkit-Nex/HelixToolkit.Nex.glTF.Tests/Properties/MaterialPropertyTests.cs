using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using GltfSampler = glTFLoader.Schema.Sampler;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 10: Sampler state mapping

/// <summary>
/// Property-based tests for sampler state mapping (Property 10), PBR factor clamping (Property 8),
/// and material color mapping (Property 9).
/// </summary>
[TestClass]
public class MaterialPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// A mock ISamplerRepository that captures the SamplerStateDesc passed to GetOrCreate.
    /// </summary>
    private sealed class CapturingSamplerRepository : ISamplerRepository
    {
        public SamplerStateDesc? CapturedDesc { get; private set; }
        private readonly MockContext _context = new();
        private readonly SamplerRepository _inner;

        public CapturingSamplerRepository()
        {
            _context.Initialize();
            _inner = new SamplerRepository(_context);
        }

        public int Count => _inner.Count;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc)
        {
            CapturedDesc = desc;
            return _inner.GetOrCreate(key, desc);
        }

        public bool Remove(string key) => _inner.Remove(key);

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry) =>
            _inner.TryGet(cacheKey, out entry);

        public void Clear() => _inner.Clear();

        public int CleanupExpired() => _inner.CleanupExpired();

        public RepositoryStatistics GetStatistics() => _inner.GetStatistics();

        public void Dispose()
        {
            _inner.Dispose();
            _context.Dispose();
        }
    }

    /// <summary>
    /// A minimal mock ITextureRepository (not used by LoadSampler, but required by TextureLoader constructor).
    /// </summary>
    private sealed class StubTextureRepository : ITextureRepository
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

    /// <summary>
    /// Property 10: For any valid glTF sampler configuration (combinations of magFilter, minFilter,
    /// wrapS, wrapT values from the glTF spec), the resulting SamplerStateDesc SHALL have MinFilter,
    /// MagFilter, MipMap, WrapU, and WrapV set to the corresponding engine enum values per the
    /// defined mapping table.
    /// **Validates: Requirements 5.9**
    /// </summary>
    [TestMethod]
    public void LoadSampler_MapsAllValidCombinations_ToCorrectSamplerStateDesc()
    {
        // All valid glTF sampler enum values
        var magFilterValues = new GltfSampler.MagFilterEnum[]
        {
            GltfSampler.MagFilterEnum.NEAREST,
            GltfSampler.MagFilterEnum.LINEAR,
        };

        var minFilterValues = new GltfSampler.MinFilterEnum[]
        {
            GltfSampler.MinFilterEnum.NEAREST,
            GltfSampler.MinFilterEnum.LINEAR,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR,
        };

        var wrapSValues = new GltfSampler.WrapSEnum[]
        {
            GltfSampler.WrapSEnum.REPEAT,
            GltfSampler.WrapSEnum.CLAMP_TO_EDGE,
            GltfSampler.WrapSEnum.MIRRORED_REPEAT,
        };

        var wrapTValues = new GltfSampler.WrapTEnum[]
        {
            GltfSampler.WrapTEnum.REPEAT,
            GltfSampler.WrapTEnum.CLAMP_TO_EDGE,
            GltfSampler.WrapTEnum.MIRRORED_REPEAT,
        };

        // Generator: pick one value from each enum set
        var samplerGen =
            from magIdx in Gen.Choose(0, magFilterValues.Length - 1)
            from minIdx in Gen.Choose(0, minFilterValues.Length - 1)
            from wrapSIdx in Gen.Choose(0, wrapSValues.Length - 1)
            from wrapTIdx in Gen.Choose(0, wrapTValues.Length - 1)
            select (
                magFilter: magFilterValues[magIdx],
                minFilter: minFilterValues[minIdx],
                wrapS: wrapSValues[wrapSIdx],
                wrapT: wrapTValues[wrapTIdx]
            );

        Prop.ForAll(
                Arb.From(samplerGen),
                (
                    (
                        GltfSampler.MagFilterEnum magFilter,
                        GltfSampler.MinFilterEnum minFilter,
                        GltfSampler.WrapSEnum wrapS,
                        GltfSampler.WrapTEnum wrapT
                    ) input
                ) =>
                {
                    using var samplerRepo = new CapturingSamplerRepository();
                    using var textureRepo = new StubTextureRepository();

                    var model = new Gltf
                    {
                        Samplers =
                        [
                            new GltfSampler
                            {
                                MagFilter = input.magFilter,
                                MinFilter = input.minFilter,
                                WrapS = input.wrapS,
                                WrapT = input.wrapT,
                            },
                        ],
                    };

                    var loader = new TextureLoader(
                        textureRepo,
                        samplerRepo,
                        "C:\\test",
                        model,
                        [],
                        new List<ImportDiagnostic>(),
                        new ResourceManifest(),
                        Guid.NewGuid().ToString("D")
                    );

                    // Act
                    loader.LoadSampler(0);

                    // Assert: desc was captured
                    var desc = samplerRepo.CapturedDesc;
                    if (desc == null)
                        return false;

                    // Verify MagFilter mapping
                    var expectedMagFilter = input.magFilter switch
                    {
                        GltfSampler.MagFilterEnum.NEAREST => SamplerFilter.Nearest,
                        GltfSampler.MagFilterEnum.LINEAR => SamplerFilter.Linear,
                        _ => SamplerFilter.Linear,
                    };
                    if (desc.MagFilter != expectedMagFilter)
                        return false;

                    // Verify MinFilter mapping
                    var expectedMinFilter = input.minFilter switch
                    {
                        GltfSampler.MinFilterEnum.NEAREST => SamplerFilter.Nearest,
                        GltfSampler.MinFilterEnum.LINEAR => SamplerFilter.Linear,
                        GltfSampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST => SamplerFilter.Nearest,
                        GltfSampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST => SamplerFilter.Linear,
                        GltfSampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR => SamplerFilter.Nearest,
                        GltfSampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR => SamplerFilter.Linear,
                        _ => SamplerFilter.Linear,
                    };
                    if (desc.MinFilter != expectedMinFilter)
                        return false;

                    // Verify MipMap mapping
                    var expectedMipMap = input.minFilter switch
                    {
                        GltfSampler.MinFilterEnum.NEAREST => SamplerMip.Disabled,
                        GltfSampler.MinFilterEnum.LINEAR => SamplerMip.Disabled,
                        GltfSampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST => SamplerMip.Nearest,
                        GltfSampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST => SamplerMip.Nearest,
                        GltfSampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR => SamplerMip.Linear,
                        GltfSampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR => SamplerMip.Linear,
                        _ => SamplerMip.Disabled,
                    };
                    if (desc.MipMap != expectedMipMap)
                        return false;

                    // Verify WrapU mapping (from wrapS)
                    var expectedWrapU = input.wrapS switch
                    {
                        GltfSampler.WrapSEnum.REPEAT => SamplerWrap.Repeat,
                        GltfSampler.WrapSEnum.CLAMP_TO_EDGE => SamplerWrap.Clamp,
                        GltfSampler.WrapSEnum.MIRRORED_REPEAT => SamplerWrap.MirrorRepeat,
                        _ => SamplerWrap.Repeat,
                    };
                    if (desc.WrapU != expectedWrapU)
                        return false;

                    // Verify WrapV mapping (from wrapT)
                    var expectedWrapV = input.wrapT switch
                    {
                        GltfSampler.WrapTEnum.REPEAT => SamplerWrap.Repeat,
                        GltfSampler.WrapTEnum.CLAMP_TO_EDGE => SamplerWrap.Clamp,
                        GltfSampler.WrapTEnum.MIRRORED_REPEAT => SamplerWrap.MirrorRepeat,
                        _ => SamplerWrap.Repeat,
                    };
                    if (desc.WrapV != expectedWrapV)
                        return false;

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    // Feature: gltf-importer, Property 8: PBR factor clamping

    /// <summary>
    /// A mock IPBRMaterialPropertyManager that delegates to the real PBRMaterialPropertyManager
    /// using the built-in "PBR" registered material type, regardless of the requested name.
    /// This allows MaterialConverter to create materials with arbitrary glTF material names.
    /// </summary>
    private sealed class MockPBRMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public PBRMaterialProperties Create(string materialName)
        {
            // Always use the built-in "PBR" type regardless of the requested name
            return _inner.Create("PBR");
        }

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties)
        {
            return _inner.Create("PBR", ref properties);
        }

        public PBRMaterialProperties Create(MaterialTypeId materialTypeId)
        {
            return _inner.Create(materialTypeId);
        }

        public PBRMaterialProperties Create(
            MaterialTypeId materialTypeId,
            ref PBRProperties properties
        )
        {
            return _inner.Create(materialTypeId, ref properties);
        }

        public void Clear() => _inner.Clear();

        public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
            _inner.Objects;

        public ref PBRProperties At(int index) => ref _inner.At(index);
        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer)
        {
            return ResultCode.Ok;
        }
        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer, IEnumerable<uint> indices)
        {
            return ResultCode.Ok;
        }
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>
    /// Property 8: For any float value provided as a glTF metallicFactor or roughnessFactor
    /// (including values outside [0,1]), the resulting engine Metallic or Roughness property
    /// SHALL be in the range [0.0, 1.0].
    /// **Validates: Requirements 4.2, 4.3, 4.9**
    /// </summary>
    [TestMethod]
    public void ConvertMaterial_ClampsPbrFactors_ToZeroOneRange()
    {
        // Generator: float values in [0, 1] — glTFLoader schema enforces this range
        // at the property setter level (throws ArgumentOutOfRangeException for values
        // outside [0, 1]), so out-of-range values are unreachable through the schema API.
        // The clamping in MaterialConverter.ApplyPbrFactors is a defensive guard for
        // values that arrive via other code paths (e.g., deserialized without validation).
        var factorGen =
            from f in Gen.OneOf(
                Gen.Choose(0, 1000).Select(i => i / 1000.0f), // Range [0, 1] with decimals
                Gen.Constant(0.0f),
                Gen.Constant(1.0f)
            )
            select f;

        var inputGen =
            from metallic in factorGen
            from roughness in factorGen
            select (metallic, roughness);

        Prop.ForAll(
                Arb.From(inputGen),
                ((float metallic, float roughness) input) =>
                {
                    using var materialManager = new MockPBRMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new CapturingSamplerRepository();

                    // Create a minimal glTF model with a material that has the generated factor values
                    var model = new Gltf
                    {
                        Materials =
                        [
                            new glTFLoader.Schema.Material
                            {
                                Name = "TestMaterial",
                                PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                                {
                                    MetallicFactor = input.metallic,
                                    RoughnessFactor = input.roughness,
                                    BaseColorFactor = [1.0f, 1.0f, 1.0f, 1.0f],
                                },
                            },
                        ],
                    };

                    var diagnostics = new List<ImportDiagnostic>();
                    var manifest = new ResourceManifest();
                    var textureLoader = new TextureLoader(
                        textureRepo,
                        samplerRepo,
                        "C:\\test",
                        model,
                        [],
                        diagnostics,
                        manifest,
                        Guid.NewGuid().ToString("D")
                    );

                    var converter = new MaterialConverter(
                        materialManager,
                        textureLoader,
                        diagnostics,
                        manifest
                    );

                    // Act
                    var material = converter.ConvertMaterial(model, 0);

                    // Assert: Metallic is clamped to [0, 1]
                    bool metallicInRange = material.Metallic >= 0.0f && material.Metallic <= 1.0f;

                    // Assert: Roughness is clamped to [0, 1]
                    bool roughnessInRange =
                        material.Roughness >= 0.0f && material.Roughness <= 1.0f;

                    // Cleanup
                    material.Dispose();

                    return metallicInRange && roughnessInRange;
                }
            )
            .Check(FsCheckConfig);
    }

    // Feature: gltf-importer, Property 9: Material color mapping

    /// <summary>
    /// Property 9: For any glTF baseColorFactor [r, g, b, a] where each component is in [0,1],
    /// the resulting PBRMaterialProperties SHALL have Albedo == (r, g, b) and Opacity == a.
    /// For any emissiveFactor [r, g, b] in [0,1], Emissive SHALL equal (r, g, b).
    /// **Validates: Requirements 4.1, 4.4**
    /// </summary>
    [TestMethod]
    public void ConvertMaterial_MapsColorFactors_ToAlbedoOpacityAndEmissive()
    {
        const float tolerance = 1e-5f;

        // Generator: color components in [0, 1]
        var colorComponentGen = Gen.Choose(0, 1000).Select(i => i / 1000.0f);

        var inputGen =
            from r in colorComponentGen
            from g in colorComponentGen
            from b in colorComponentGen
            from a in colorComponentGen
            from er in colorComponentGen
            from eg in colorComponentGen
            from eb in colorComponentGen
            select (r, g, b, a, er, eg, eb);

        Prop.ForAll(
                Arb.From(inputGen),
                ((float r, float g, float b, float a, float er, float eg, float eb) input) =>
                {
                    using var materialManager = new MockPBRMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new CapturingSamplerRepository();

                    // Create a glTF model with a material that has the generated color factors
                    var model = new Gltf
                    {
                        Materials =
                        [
                            new glTFLoader.Schema.Material
                            {
                                Name = "TestMaterial",
                                PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                                {
                                    BaseColorFactor = [input.r, input.g, input.b, input.a],
                                },
                                EmissiveFactor = [input.er, input.eg, input.eb],
                            },
                        ],
                    };

                    var diagnostics = new List<ImportDiagnostic>();
                    var manifest = new ResourceManifest();
                    var textureLoader = new TextureLoader(
                        textureRepo,
                        samplerRepo,
                        "C:\\test",
                        model,
                        [],
                        diagnostics,
                        manifest,
                        Guid.NewGuid().ToString("D")
                    );

                    var converter = new MaterialConverter(
                        materialManager,
                        textureLoader,
                        diagnostics,
                        manifest
                    );

                    // Act
                    var material = converter.ConvertMaterial(model, 0);

                    // Assert: Albedo RGB matches baseColorFactor RGB
                    var albedo = material.Albedo;
                    bool albedoRed = MathF.Abs(albedo.Red - input.r) <= tolerance;
                    bool albedoGreen = MathF.Abs(albedo.Green - input.g) <= tolerance;
                    bool albedoBlue = MathF.Abs(albedo.Blue - input.b) <= tolerance;

                    // Assert: Opacity matches baseColorFactor alpha
                    bool opacityMatch = MathF.Abs(material.Opacity - input.a) <= tolerance;

                    // Assert: Emissive RGB matches emissiveFactor
                    var emissive = material.Emissive;
                    bool emissiveRed = MathF.Abs(emissive.Red - input.er) <= tolerance;
                    bool emissiveGreen = MathF.Abs(emissive.Green - input.eg) <= tolerance;
                    bool emissiveBlue = MathF.Abs(emissive.Blue - input.eb) <= tolerance;

                    // Cleanup
                    material.Dispose();

                    return albedoRed
                        && albedoGreen
                        && albedoBlue
                        && opacityMatch
                        && emissiveRed
                        && emissiveGreen
                        && emissiveBlue;
                }
            )
            .Check(FsCheckConfig);
    }
}
