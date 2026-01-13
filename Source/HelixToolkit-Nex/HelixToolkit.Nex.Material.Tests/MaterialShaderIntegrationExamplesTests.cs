using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Material.Tests;

[TestClass]
[TestCategory("Examples")]
[TestCategory("ShaderBuilding")]
[TestCategory("GPURequired")]
public class MaterialShaderIntegrationExamplesTests
{
    private IContext? _context;

    [TestInitialize]
    public void Initialize()
    {
        // Create headless Vulkan context for testing, similar to MaterialShaderBuilderIntegrationTests
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            EnableValidation = false, // Disable validation for faster tests
        };
        _context = VulkanBuilder.CreateHeadless(config);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
    }

    [TestMethod]
    public void TestExample1_BasicPBRMaterial()
    {
        // Act & Assert (ensure no exception is thrown)
        MaterialShaderIntegrationExamples.Example1_BasicPBRMaterial(_context!);
    }

    [TestMethod]
    public void TestExample2_TexturedPBRMaterial()
    {
        // Arrange
        // Create dummy 1x1 textures for the example
        var textureDesc = new TextureDesc
        {
            Type = TextureType.Texture2D,
            Format = Format.RGBA_UN8,
            Dimensions = new Dimensions(1, 1, 1),
            Usage = TextureUsageBits.Sampled,
            NumMipLevels = 1,
            Storage = StorageType.Device,
        };

        var samplerDesc = new SamplerStateDesc
        {
            MinFilter = SamplerFilter.Linear,
            MagFilter = SamplerFilter.Linear,
            WrapU = SamplerWrap.Repeat,
            WrapV = SamplerWrap.Repeat,
        };

        _context!.CreateTexture(textureDesc, out var albedoTexture);
        _context.CreateTexture(textureDesc, out var normalTexture);
        _context.CreateSampler(samplerDesc, out var sampler);

        try
        {
            // Act
            MaterialShaderIntegrationExamples.Example2_TexturedPBRMaterial(
                _context!,
                albedoTexture,
                normalTexture,
                sampler
            );
        }
        finally
        {
            // Cleanup resources created for this test
            albedoTexture.Dispose();
            normalTexture.Dispose();
            sampler.Dispose();
        }
    }

    [TestMethod]
    public void TestExample3_CustomMaterialShader()
    {
        // Act & Assert
        MaterialShaderIntegrationExamples.Example3_CustomMaterialShader(_context!);
    }

    [TestMethod]
    public void TestExample4_MaterialFactory()
    {
        // Act & Assert
        // This relies on "PBR" being a registered material name, which should be true if PbrMaterial is loaded
        MaterialShaderIntegrationExamples.Example4_MaterialFactory(_context!);
    }

    [TestMethod]
    public void TestExample5_MaterialLibrary()
    {
        // Act
        var library = MaterialShaderIntegrationExamples.Example5_MaterialLibrary(_context!);

        // Assert
        Assert.IsNotNull(library);
        Assert.IsTrue(library.Count > 0, "Library should contain materials");
        Assert.IsTrue(library.ContainsKey("Metal"), "Library should contain 'Metal'");
        Assert.IsTrue(library.ContainsKey("Plastic"), "Library should contain 'Plastic'");
    }

    [TestMethod]
    public void TestExample6_DynamicMaterialUpdate()
    {
        // Arrange
        var material = new PbrMaterial();
        var pipelineDesc = new RenderPipelineDesc();
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        // Initialize first
        material.InitializePipeline(_context!, pipelineDesc);

        // Act
        MaterialShaderIntegrationExamples.Example6_DynamicMaterialUpdate(_context!, material);

        // Assert
        Assert.IsTrue(material.Pipeline.Valid, "Material pipeline should be valid after update");
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void TestExample7_ConditionalShaderGeneration(bool useNormalMap, bool useEmissive)
    {
        // Act & Assert
        MaterialShaderIntegrationExamples.Example7_ConditionalShaderGeneration(
            _context!,
            useNormalMap,
            useEmissive
        );
    }

    [TestMethod]
    public void TestExample8_QualityVariants()
    {
        // Act
        var variants = MaterialShaderIntegrationExamples.Example8_QualityVariants(_context!);

        // Assert
        Assert.IsNotNull(variants);
        Assert.IsTrue(variants.ContainsKey("High"));
        Assert.IsTrue(variants.ContainsKey("Medium"));
        Assert.IsTrue(variants.ContainsKey("Low"));

        foreach (var variant in variants.Values)
        {
            Assert.IsTrue(variant.Success, "Variant generation should succeed");
            Assert.IsTrue(variant.VertexShader.Valid, "Variant vertex shader should be valid");
            Assert.IsTrue(variant.FragmentShader.Valid, "Variant fragment shader should be valid");
        }
    }
}
