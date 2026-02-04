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
    public void TestExample3_CustomMaterialShader()
    {
        // Act & Assert
        MaterialShaderIntegrationExamples.Example3_CustomMaterialShader(_context!);
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
