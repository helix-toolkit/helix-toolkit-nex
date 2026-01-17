using System.Numerics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Material.Tests;

[TestClass]
public class MaterialShaderBuilderForwardPlusTests
{
    [TestMethod]
    public void TestBasicShaderBuilding()
    {
        var builder = new MaterialShaderBuilder();
        var result = builder.BuildFragmentShader();

        Assert.IsTrue(result.Success, "Basic shader should compile successfully");
        Assert.IsNotNull(result.Source);
    }

    [TestMethod]
    public void TestBindlessVertexShader()
    {
        var builder = new MaterialShaderBuilder().WithBindlessVertices(true);

        var vertResult = builder.BuildFragmentShader();

        Assert.IsTrue(vertResult.Success, "Bindless vertex shader should compile");
        Assert.IsTrue(
            vertResult.Source!.Contains("buffer_reference"),
            "Should include buffer_reference extension"
        );
    }

    [TestMethod]
    public void TestForwardPlusShader()
    {
        var config = new ForwardPlusConfig { TileSize = 16, MaxLightsPerTile = 128 };

        var builder = new MaterialShaderBuilder().WithForwardPlus(true, config);

        var result = builder.BuildFragmentShader();

        Assert.IsTrue(result.Success, "Forward+ shader should compile");
        Assert.IsTrue(result.Source!.Contains("Light"), "Should define Light structure");
        Assert.IsTrue(
            result.Source.Contains("LightGridTile"),
            "Should define LightGridTile structure"
        );
        Assert.IsTrue(result.Source.Contains("tileCoord"), "Should include tile calculation");
    }

    [TestMethod]
    public void TestBindlessVerticesWithForwardPlus()
    {
        var builder = new MaterialShaderBuilder().WithBindlessVertices(true).WithForwardPlus(true);

        var fragResult = builder.BuildFragmentShader();

        Assert.IsTrue(
            fragResult.Success,
            "Combined bindless vertices and Forward+ shader should compile"
        );
        Assert.IsTrue(
            fragResult.Source!.Contains("vertexBufferAddress"),
            "Should include vertex buffer address in push constants"
        );
        Assert.IsTrue(
            fragResult.Source.Contains("lightBufferAddress"),
            "Should include light buffer address in push constants"
        );
    }

    [TestMethod]
    public void TestLightCullingComputeShader()
    {
        var config = ForwardPlusConfig.Default;
        var shaderSource = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);

        Assert.IsNotNull(shaderSource);
        Assert.IsTrue(shaderSource.Contains("local_size_x"), "Should define work group size");
        Assert.IsTrue(
            shaderSource.Contains("createTileFrustum"),
            "Should include frustum creation"
        );
        Assert.IsTrue(
            shaderSource.Contains("sphereInsideFrustum"),
            "Should include frustum culling test"
        );
        Assert.IsTrue(
            shaderSource.Contains("atomicAdd"),
            "Should use atomic operations for light lists"
        );
    }

    [TestMethod]
    public void TestCustomShaderCode()
    {
        var builder = new MaterialShaderBuilder()
            .WithForwardPlus(true)
            .WithCustomCode("// Custom code section\nfloat customFunction() { return 1.0; }");

        var result = builder.BuildFragmentShader();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Source!.Contains("customFunction"), "Should include custom code");
    }

    [TestMethod]
    public void TestMaterialPropertyDefines()
    {
        var material = new PbrMaterialProperties();
        // Note: Cannot actually create texture handles in unit tests without a context,
        // so we'll just verify the builder works with the material API
        var builder = new MaterialShaderBuilder().ForMaterial(material);

        var result = builder.BuildFragmentShader();

        Assert.IsTrue(result.Success);
        // The defines should be applied during compilation
    }

    [TestMethod]
    public void TestDifferentTileSizes()
    {
        var tileSizes = new uint[] { 8, 16, 32, 64 };

        foreach (var tileSize in tileSizes)
        {
            var config = new ForwardPlusConfig { TileSize = tileSize };
            var shaderSource = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);

            Assert.IsTrue(
                shaderSource.Contains($"#define TILE_SIZE {tileSize}"),
                $"Should define TILE_SIZE as {tileSize}"
            );
        }
    }

    [TestMethod]
    public void TestMaxLightsPerTile()
    {
        var maxLights = new uint[] { 64, 128, 256, 512 };

        foreach (var maxLight in maxLights)
        {
            var config = new ForwardPlusConfig { MaxLightsPerTile = maxLight };
            var shaderSource = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);

            Assert.IsTrue(
                shaderSource.Contains($"#define MAX_LIGHTS_PER_TILE {maxLight}"),
                $"Should define MAX_LIGHTS_PER_TILE as {maxLight}"
            );
        }
    }
}

[TestClass]
[TestCategory("GPURequired")]
public class MaterialShaderPipelineForwardPlusTests
{
    private static IContext? _context;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        // Create headless Vulkan context for testing
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            EnableValidation = false, // Disable validation for faster tests
        };
        _context = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _context?.Dispose();
    }

    [TestMethod]
    public void TestMaterialPipelineBuilding()
    {
        if (_context == null)
        {
            Assert.Inconclusive("Vulkan context not available for this test");
        }

        var builder = new MaterialShaderBuilder().WithForwardPlus(true).WithBindlessVertices(true);

        // This requires an active context to create shader modules
        var result = builder.BuildMaterialPipeline(_context!, "TestPipeline");

        Assert.IsTrue(result.Success, "Material pipeline build should succeed");
        Assert.IsTrue(result.VertexShader.Valid, "Vertex shader should be valid");
        Assert.IsTrue(result.FragmentShader.Valid, "Fragment shader should be valid");

        // Cleanup resources created by the test
        result.VertexShader.Dispose();
        result.FragmentShader.Dispose();
    }

    [TestMethod]
    public void TestDefaultMaterialPipelineBuilding()
    {
        if (_context == null)
        {
            Assert.Inconclusive("Vulkan context not available for this test");
        }

        var builder = new MaterialShaderBuilder();

        // This requires an active context to create shader modules
        var result = builder.BuildMaterialPipeline(_context!, "DefaultPipeline");

        Assert.IsTrue(result.Success, "Default material pipeline build should succeed");
        Assert.IsTrue(result.VertexShader.Valid, "Vertex shader should be valid");
        Assert.IsTrue(result.FragmentShader.Valid, "Fragment shader should be valid");

        // Cleanup resources created by the test
        result.VertexShader.Dispose();
        result.FragmentShader.Dispose();
    }
}

[TestClass]
public class GpuStructureTests
{
    [TestMethod]
    public void TestGpuVertexSize()
    {
        // Should be 64 bytes (16-byte aligned)
        Assert.AreEqual(64u, Vertex.SizeInBytes);
    }

    [TestMethod]
    public void TestGpuLightSize()
    {
        // Should be 64 bytes (16-byte aligned)
        Assert.AreEqual(64u, Light.SizeInBytes);
    }

    [TestMethod]
    public void TestLightGridTileSize()
    {
        // Should be 8 bytes
        Assert.AreEqual(8u, LightGridTile.SizeInBytes);
    }

    [TestMethod]
    public void TestForwardPlusConstantsAlignment()
    {
        // Should be properly aligned for push constants
        Assert.IsTrue(
            ForwardPlusConstants.SizeInBytes % 4 == 0,
            "Push constants must be 4-byte aligned"
        );
    }

    [TestMethod]
    public void TestGpuVertexFieldLayout()
    {
        // Verify structure aligns correctly
        var vertex = new Vertex
        {
            Position = new Vector3(1, 2, 3),
            Normal = new Vector3(0, 1, 0),
            TexCoord = new Vector2(0.5f, 0.5f),
            Tangent = new Vector3(1, 0, 0),
        };

        Assert.AreEqual(new Vector3(1, 2, 3), vertex.Position);
        Assert.AreEqual(new Vector3(0, 1, 0), vertex.Normal);
    }
}
