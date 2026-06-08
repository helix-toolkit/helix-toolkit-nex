using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;
using Newtonsoft.Json.Linq;
using static HelixToolkit.Nex.Pool<
    HelixToolkit.Nex.Material.MaterialPropertyResource,
    HelixToolkit.Nex.Shaders.PBRProperties
>;
using GltfTexture = glTFLoader.Schema.Texture;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for MaterialConverter: default values, alpha modes, doubleSided, and missing textures.
/// </summary>
[TestClass]
public class MaterialConverterTests
{
    /// <summary>
    /// A mock IPBRMaterialPropertyManager that delegates to the real PBRMaterialPropertyManager
    /// but maps any material name to the registered "PBR" shading mode.
    /// </summary>
    private sealed class MockMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public IReadOnlyList<PoolEntry> Objects => _inner.Objects;

        public PBRMaterialProperties Create(string materialName)
        {
            // Always use the registered PBR shading mode regardless of the name
            return _inner.Create(PBRShadingMode.PBR);
        }

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties)
        {
            return _inner.Create(PBRShadingMode.PBR, ref properties);
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

        public ref PBRProperties At(int index) => ref _inner.At(index);

        public void Clear() => _inner.Clear();
        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer)
        {
            return ResultCode.Ok;
        }

        public ResultCode UploadDynamic(
            ElementBuffer<PBRProperties> buffer,
            IEnumerable<uint> indices
        )
        {
            return ResultCode.Ok;
        }
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>
    /// A stub ITextureRepository that always returns TextureRef.Null (simulates missing textures).
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

    /// <summary>
    /// A stub ISamplerRepository that always returns SamplerRef.Null.
    /// </summary>
    private sealed class NullSamplerRepository : ISamplerRepository
    {
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

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

    private MockMaterialPropertyManager _materialManager = null!;
    private NullTextureRepository _textureRepo = null!;
    private NullSamplerRepository _samplerRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _materialManager = new MockMaterialPropertyManager();
        _textureRepo = new NullTextureRepository();
        _samplerRepo = new NullSamplerRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _materialManager?.Dispose();
        _textureRepo?.Dispose();
        _samplerRepo?.Dispose();
    }

    private MaterialConverter CreateConverter(List<ImportDiagnostic> diagnostics)
    {
        var model = new Gltf(); // Empty model for TextureLoader
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

    private MaterialConverter CreateConverterWithModel(
        Gltf model,
        List<ImportDiagnostic> diagnostics
    )
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

    // -------------------------------------------------------------------------
    // Test 1: GetDefaultMaterial returns correct default values
    // Validates: Requirement 4.5
    // -------------------------------------------------------------------------

    [TestMethod]
    public void GetDefaultMaterial_ReturnsCorrectDefaults()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var converter = CreateConverter(diagnostics);

        var material = converter.GetDefaultMaterial();

        Assert.IsNotNull(material);
        Assert.IsTrue(material.Valid);

        // Albedo should be (1, 1, 1)
        Assert.AreEqual(1.0f, material.Albedo.Red, 1e-5f, "Albedo.Red should be 1.0");
        Assert.AreEqual(1.0f, material.Albedo.Green, 1e-5f, "Albedo.Green should be 1.0");
        Assert.AreEqual(1.0f, material.Albedo.Blue, 1e-5f, "Albedo.Blue should be 1.0");

        // Metallic should be 1.0
        Assert.AreEqual(1.0f, material.Metallic, 1e-5f, "Metallic should be 1.0");

        // Roughness should be 1.0
        Assert.AreEqual(1.0f, material.Roughness, 1e-5f, "Roughness should be 1.0");

        // Opacity should be 1.0
        Assert.AreEqual(1.0f, material.Opacity, 1e-5f, "Opacity should be 1.0");

        // Emissive should be (0, 0, 0)
        Assert.AreEqual(0.0f, material.Emissive.Red, 1e-5f, "Emissive.Red should be 0.0");
        Assert.AreEqual(0.0f, material.Emissive.Green, 1e-5f, "Emissive.Green should be 0.0");
        Assert.AreEqual(0.0f, material.Emissive.Blue, 1e-5f, "Emissive.Blue should be 0.0");

        material.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 2: Alpha mode BLEND -> metadata.AlphaMode == AlphaMode.Blend
    // Validates: Requirement 4.6
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterialWithMetadata_AlphaModeBlend_SetsMetadataAlphaModeBlend()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "BlendMaterial",
                    AlphaMode = glTFLoader.Schema.Material.AlphaModeEnum.BLEND,
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.AreEqual(
            AlphaMode.Blend,
            result.Metadata.AlphaMode,
            "AlphaMode should be Blend when glTF material specifies BLEND."
        );

        result.Material.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 3: Alpha mode MASK -> metadata.AlphaMode == AlphaMode.Mask with correct cutoff
    // Validates: Requirements 4.7, 4.8
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterialWithMetadata_AlphaModeMask_SetsMetadataAlphaModeMaskWithDefaultCutoff()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "MaskMaterial",
                    AlphaMode = glTFLoader.Schema.Material.AlphaModeEnum.MASK,
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    // AlphaCutoff defaults to 0.5 per glTF spec
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.AreEqual(
            AlphaMode.Mask,
            result.Metadata.AlphaMode,
            "AlphaMode should be Mask when glTF material specifies MASK."
        );
        Assert.AreEqual(
            0.5f,
            result.Metadata.AlphaCutoff,
            1e-5f,
            "AlphaCutoff should default to 0.5 when not explicitly specified."
        );

        result.Material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterialWithMetadata_AlphaModeMask_UsesSpecifiedCutoff()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "MaskMaterialCustomCutoff",
                    AlphaMode = glTFLoader.Schema.Material.AlphaModeEnum.MASK,
                    AlphaCutoff = 0.75f,
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.AreEqual(AlphaMode.Mask, result.Metadata.AlphaMode);
        Assert.AreEqual(
            0.75f,
            result.Metadata.AlphaCutoff,
            1e-5f,
            "AlphaCutoff should match the specified value of 0.75."
        );

        result.Material.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 4: DoubleSided flag handling
    // Validates: Requirement 4.7
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterialWithMetadata_DoubleSidedTrue_SetsMetadataDoubleSided()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "DoubleSidedMaterial",
                    DoubleSided = true,
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.IsTrue(
            result.Metadata.DoubleSided,
            "DoubleSided should be true when glTF material specifies doubleSided=true."
        );

        result.Material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterialWithMetadata_DoubleSidedFalse_MetadataDoubleSidedIsFalse()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "SingleSidedMaterial",
                    DoubleSided = false,
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.IsFalse(
            result.Metadata.DoubleSided,
            "DoubleSided should be false when glTF material specifies doubleSided=false."
        );

        result.Material.Dispose();
    }

    // -------------------------------------------------------------------------
    // Test 5: Missing texture reference produces warning and TextureRef.Null
    // Validates: Requirement 7.2
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterial_MissingTextureReference_ProducesWarningAndTextureRefNull()
    {
        var diagnostics = new List<ImportDiagnostic>();

        // Model with a material that references a texture, but the texture references
        // an image that doesn't exist (TextureLoader will return TextureRef.Null)
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "TexturedMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        BaseColorTexture = new TextureInfo { Index = 0 },
                    },
                },
            ],
            Textures = [new GltfTexture { Source = 0 }],
            // Images array is null - the texture source references a non-existent image
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        // The AlbedoMap should be TextureRef.Null since the image couldn't be loaded
        Assert.AreEqual(
            TextureRef.Null,
            material.AlbedoMap,
            "AlbedoMap should be TextureRef.Null when the referenced texture cannot be loaded."
        );

        // A warning diagnostic should have been added
        Assert.IsTrue(
            diagnostics.Count > 0,
            "At least one diagnostic should be produced for a missing texture reference."
        );
        Assert.IsTrue(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning),
            "A warning diagnostic should be produced for a missing texture reference."
        );

        material.Dispose();
    }

    // -------------------------------------------------------------------------
    // Additional: Alpha mode OPAQUE is the default
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterialWithMetadata_DefaultAlphaMode_IsOpaque()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "OpaqueMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var result = converter.ConvertMaterialWithMetadata(model, 0);

        Assert.AreEqual(
            AlphaMode.Opaque,
            result.Metadata.AlphaMode,
            "Default alpha mode should be Opaque."
        );
        Assert.IsFalse(result.Metadata.DoubleSided, "Default DoubleSided should be false.");

        result.Material.Dispose();
    }

    // -------------------------------------------------------------------------
    // KHR_materials_ior Unit Tests
    // Validates: Requirements 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4, 2.5,
    //            3.1, 3.2, 3.4, 6.2, 6.3
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ConvertMaterial_IOR_1_5_ProducesReflectance_0_04()
    {
        // IOR 1.5 is the glTF default → F0 = ((1.5-1)/(1.5+1))^2 = (0.5/2.5)^2 = 0.04
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "IOR_1_5_Material",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = 1.5f },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.04f,
            material.Reflectance,
            1e-6f,
            "IOR 1.5 should produce reflectance 0.04."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_IOR_1_0_ProducesReflectance_0_0()
    {
        // IOR 1.0 (vacuum/air) → F0 = ((1.0-1)/(1.0+1))^2 = 0.0
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "IOR_1_0_Material",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = 1.0f },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.0f,
            material.Reflectance,
            1e-6f,
            "IOR 1.0 should produce reflectance 0.0."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_IOR_2_42_ProducesReflectance_Approximately_0_1727()
    {
        // IOR 2.42 (diamond) → F0 = ((2.42-1)/(2.42+1))^2 = (1.42/3.42)^2 ≈ 0.1727
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "IOR_Diamond_Material",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = 2.42f },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        float expected = (2.42f - 1.0f) / (2.42f + 1.0f);
        expected *= expected; // ≈ 0.1727

        Assert.AreEqual(
            expected,
            material.Reflectance,
            1e-4f,
            "IOR 2.42 (diamond) should produce reflectance ≈ 0.1727."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_MissingIorExtension_PreservesDefaultReflectance_0_04()
    {
        // No KHR_materials_ior extension → reflectance stays at default 0.04
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "NoIorMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.04f,
            material.Reflectance,
            1e-6f,
            "Missing KHR_materials_ior extension should preserve default reflectance 0.04."
        );

        material.Dispose();
    }

    [TestMethod]
    public void GetDefaultMaterial_SetsReflectance_0_04()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var converter = CreateConverter(diagnostics);

        var material = converter.GetDefaultMaterial();

        Assert.AreEqual(
            0.04f,
            material.Reflectance,
            1e-6f,
            "GetDefaultMaterial() should set reflectance to 0.04."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_MissingIorProperty_UsesDefaultIOR_1_5()
    {
        // Extension present but ior property missing → use default IOR 1.5 → F0 = 0.04
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "MissingIorPropMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject(), // No ior property
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.04f,
            material.Reflectance,
            1e-6f,
            "Missing ior property in extension should use default IOR 1.5 (reflectance = 0.04)."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_InvalidIorType_UsesDefaultIOR_1_5()
    {
        // Extension present but ior is a string → use default IOR 1.5 → F0 = 0.04
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "InvalidIorTypeMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = "not_a_number" },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.04f,
            material.Reflectance,
            1e-6f,
            "Invalid ior type (string) should use default IOR 1.5 (reflectance = 0.04)."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_IOR_BelowOne_ClampsToOne_ProducesReflectance_0_0()
    {
        // IOR < 1.0 → clamp to 1.0 → F0 = 0.0
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "IOR_Below_One_Material",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = 0.5f },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        Assert.AreEqual(
            0.0f,
            material.Reflectance,
            1e-6f,
            "IOR < 1.0 should clamp to 1.0 and produce reflectance 0.0."
        );

        material.Dispose();
    }

    [TestMethod]
    public void ConvertMaterial_IOR_BelowOne_EmitsExactlyOneDiagnosticWarning()
    {
        // IOR < 1.0 → should emit exactly one diagnostic warning
        var diagnostics = new List<ImportDiagnostic>();
        var model = new Gltf
        {
            Materials =
            [
                new glTFLoader.Schema.Material
                {
                    Name = "IOR_Below_One_Warning_Material",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness(),
                    Extensions = new Dictionary<string, object>
                    {
                        ["KHR_materials_ior"] = new JObject { ["ior"] = -2.0f },
                    },
                },
            ],
        };

        var converter = CreateConverterWithModel(model, diagnostics);
        var material = converter.ConvertMaterial(model, 0);

        var iorWarnings = diagnostics
            .Where(d =>
                d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("KHR_materials_ior")
            )
            .ToList();

        Assert.AreEqual(
            1,
            iorWarnings.Count,
            "IOR < 1.0 should emit exactly one diagnostic warning."
        );
        Assert.IsTrue(
            iorWarnings[0].Message.Contains("-2"),
            "Warning message should contain the original IOR value."
        );

        material.Dispose();
    }
}
