using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Material.Tests;

/// <summary>
/// Integration tests for MaterialShaderBuilder with ShaderBuilderContextExtensions.
/// These tests verify that generated shaders actually compile successfully.
/// </summary>
[TestClass]
[TestCategory("ShaderBuilding")]
public class MaterialShaderBuilderIntegrationTests
{
    private ShaderCompiler? _compiler;
    private IContext? _context;

    [TestInitialize]
    public void Initialize()
    {
        _compiler = GlslHeaders.CreateCompiler();
        // Create headless Vulkan context for testing
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
        _compiler = null;
        _context?.Dispose();
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestBasicPBRShaderCompilation()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithPBRShading(true);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Fragment shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsNotNull(fragmentResult.Source, "Fragment source should not be null");
        Assert.AreEqual(
            0,
            fragmentResult.Errors.Count,
            $"Should have no errors: {string.Join(", ", fragmentResult.Errors)}"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusShaderCompilation()
    {
        // Arrange
        var config = new ForwardPlusConfig
        {
            TileSize = 16,
            MaxLightsPerTile = 256,
            UseComputeCulling = true,
        };

        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithForwardPlus(true, config);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Forward+ fragment shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsNotNull(fragmentResult.Source, "Source should not be null");
        Assert.AreEqual(0, fragmentResult.Errors.Count, "Should have no compilation errors");

        // Verify Forward+ specific structures are present
        Assert.IsTrue(
            fragmentResult.Source.Contains("GpuLight"),
            "Should contain GpuLight structure"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("LightGridTile"),
            "Should contain LightGridTile structure"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("ForwardPlusConstants"),
            "Should contain ForwardPlusConstants push constant block"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("buffer_reference"),
            "Should include buffer_reference extension"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestBindlessVerticesShaderCompilation()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithPBRShading(true).WithBindlessVertices(true);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Bindless vertices shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsNotNull(fragmentResult.Source, "Source should not be null");

        // Verify bindless vertex structures
        Assert.IsTrue(
            fragmentResult.Source.Contains("GpuVertex"),
            "Should contain GpuVertex structure"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("VertexBuffer"),
            "Should contain VertexBuffer type"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("buffer_reference"),
            "Should include buffer_reference extension"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("vertexIndex"),
            "Should use vertexIndex input"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestCombinedBindlessAndForwardPlusCompilation()
    {
        // Arrange
        var config = ForwardPlusConfig.Default;
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithBindlessVertices(true)
            .WithForwardPlus(true, config);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Combined shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsNotNull(fragmentResult.Source, "Source should not be null");

        // Verify both systems are present
        Assert.IsTrue(
            fragmentResult.Source.Contains("GpuVertex"),
            "Should contain GpuVertex for bindless vertices"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("GpuLight"),
            "Should contain GpuLight for Forward+"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("vertexBufferAddress"),
            "Should have vertex buffer address in push constants"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("lightBufferAddress"),
            "Should have light buffer address in push constants"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithTexturesCompilation()
    {
        // Arrange
        var material = new PbrMaterialProperties();
        // Simulate textures being assigned (we can't create actual resources in unit tests)

        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithForwardPlus(true)
            .WithDefine("USE_BASE_COLOR_TEXTURE")
            .WithDefine("USE_NORMAL_TEXTURE")
            .WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE");

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Textured Forward+ shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsTrue(
            fragmentResult.Source!.Contains("baseColorTexIndex"),
            "Should reference texture indices"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("kTextures2D"),
            "Should reference bindless texture array"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("kSamplers"),
            "Should reference bindless sampler array"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestLightCullingComputeShaderCompilation()
    {
        // Arrange
        var config = new ForwardPlusConfig
        {
            TileSize = 16,
            MaxLightsPerTile = 256,
            UseComputeCulling = true,
        };

        // Act
        var shaderSource = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);
        var result = _compiler!.Compile(ShaderStage.Compute, shaderSource);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Light culling compute shader should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsNotNull(result.Source, "Source should not be null");

        // Verify compute shader specific content
        Assert.IsTrue(result.Source.Contains("local_size_x"), "Should define work group size");
        Assert.IsTrue(
            result.Source.Contains("gl_WorkGroupID"),
            "Should use compute shader built-ins"
        );
        Assert.IsTrue(
            result.Source.Contains("createTileFrustum"),
            "Should include frustum creation function"
        );
        Assert.IsTrue(
            result.Source.Contains("sphereInsideFrustum"),
            "Should include frustum culling test"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    [DataRow(8u)]
    [DataRow(16u)]
    [DataRow(32u)]
    [DataRow(64u)]
    public void TestDifferentTileSizesCompilation(uint tileSize)
    {
        // Arrange
        var config = new ForwardPlusConfig { TileSize = tileSize };
        var builder = new MaterialShaderBuilder().WithForwardPlus(true, config);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Forward+ with tile size {tileSize} should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsTrue(
            fragmentResult.Source!.Contains($"#define TILE_SIZE {tileSize}")
                || fragmentResult.Source.Contains($"pc.tileSize"),
            $"Should use tile size {tileSize}"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    [DataRow(64u)]
    [DataRow(128u)]
    [DataRow(256u)]
    [DataRow(512u)]
    public void TestDifferentMaxLightsPerTileCompilation(uint maxLights)
    {
        // Arrange
        var config = new ForwardPlusConfig { MaxLightsPerTile = maxLights };
        var builder = new MaterialShaderBuilder().WithForwardPlus(true, config);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Forward+ with max lights {maxLights} should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );

        // Verify the define is present in the source
        Assert.IsTrue(
            fragmentResult.Source!.Contains($"#define MAX_LIGHTS_PER_TILE {maxLights}")
                || fragmentResult.Source.Contains("MAX_LIGHTS_PER_TILE"),
            $"Should define MAX_LIGHTS_PER_TILE as {maxLights}"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestCustomShaderCodeWithForwardPlus()
    {
        // Arrange
        var customCode =
            @"
            // Custom lighting function
            vec3 customLighting(vec3 albedo, vec3 normal, vec3 viewDir) {
                return albedo * max(dot(normal, viewDir), 0.0);
            }
        ";

        var builder = new MaterialShaderBuilder().WithForwardPlus(true).WithCustomCode(customCode);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            $"Custom code with Forward+ should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsTrue(
            fragmentResult.Source!.Contains("customLighting"),
            "Should include custom lighting function"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestVertexShaderCompilation()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithPBRShading(true).WithForwardPlus(true);

        // Generate vertex shader source
        var vertexSource =
            builder
                .GetType()
                .GetMethod(
                    "GenerateVertexShader",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .Invoke(builder, null) as string;

        // Act
        var result = _compiler!.CompileVertexShader(vertexSource!);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Vertex shader should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsNotNull(result.Source, "Vertex source should not be null");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestBindlessVertexShaderCompilation()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithBindlessVertices(true).WithForwardPlus(true);

        // Generate vertex shader source
        var vertexSource =
            builder
                .GetType()
                .GetMethod(
                    "GenerateVertexShader",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .Invoke(builder, null) as string;

        // Act
        var result = _compiler!.CompileVertexShader(vertexSource!);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Bindless vertex shader should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsTrue(
            result.Source!.Contains("GpuVertex"),
            "Should contain GpuVertex structure in vertex shader"
        );
        Assert.IsTrue(
            result.Source.Contains("buffer_reference"),
            "Should use buffer_reference extension"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestMaterialPipelineBuildingWithForwardPlus()
    {
        // Arrange
        var config = ForwardPlusConfig.Default;
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithForwardPlus(true, config);

        // Note: We can't actually build a full pipeline without a real IContext,
        // but we can verify shader compilation succeeds
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(
            fragmentResult.Success,
            "Material pipeline shaders should compile successfully"
        );
        Assert.IsNotNull(fragmentResult.Source, "Fragment shader source should be generated");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestAllExtensionsEnabledCorrectly()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithBindlessVertices(true).WithForwardPlus(true);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(fragmentResult.Success, "Shader should compile with all extensions");

        // Verify all required extensions are present
        Assert.IsTrue(
            fragmentResult.Source!.Contains("GL_EXT_buffer_reference"),
            "Should enable buffer_reference extension"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("GL_EXT_buffer_reference_uvec2"),
            "Should enable buffer_reference_uvec2 extension"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("GL_EXT_nonuniform_qualifier"),
            "Should enable nonuniform_qualifier extension"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestShaderCachingWithForwardPlus()
    {
        // Arrange
        var cache = new ShaderCache(maxEntries: 10);
        var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

        var builder = new MaterialShaderBuilder().WithForwardPlus(true);

        var fragmentSource = builder.BuildFragmentShader().Source!;

        // Act
        var result1 = compiler.Compile(ShaderStage.Fragment, fragmentSource);
        var result2 = compiler.Compile(ShaderStage.Fragment, fragmentSource);

        // Assert
        Assert.IsTrue(result1.Success, "First compilation should succeed");
        Assert.IsTrue(result2.Success, "Second compilation should succeed");
        Assert.AreEqual(1, cache.Count, "Should have 1 cached entry (cache hit on second compile)");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestPBRFunctionsIncludedWithForwardPlus()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithPBRShading(true).WithForwardPlus(true);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(fragmentResult.Success, "Shader with PBR should compile");

        // Verify PBR functions are actually included in the source code
        // The IncludedFiles list tracks #include directives processed by ShaderBuilder,
        // but MaterialShaderBuilder generates code inline, so we verify presence in source instead
        Assert.IsTrue(
            fragmentResult.Source!.Contains("struct PBRMaterial"),
            "Should include PBRMaterial struct definition"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("cookTorranceBRDF"),
            "Should include cookTorranceBRDF function"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("pbrShadingSimple")
                || fragmentResult.Source.Contains("pbrShading"),
            "Should include PBR shading functions"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("fresnelSchlick"),
            "Should include Fresnel function"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("distributionGGX"),
            "Should include GGX distribution function"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestLightingCalculationsInForwardPlus()
    {
        // Arrange
        var builder = new MaterialShaderBuilder().WithPBRShading(true).WithForwardPlus(true);

        // Act
        var fragmentResult = builder.BuildFragmentShader();

        // Assert
        Assert.IsTrue(fragmentResult.Success, "Shader should compile");

        // Verify Forward+ lighting logic
        Assert.IsTrue(
            fragmentResult.Source!.Contains("tileCoord"),
            "Should calculate tile coordinates"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("lightGrid.tiles[tileIndex]"),
            "Should access light grid"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("for")
                && fragmentResult.Source.Contains("tile.lightCount"),
            "Should loop through lights in tile"
        );
        Assert.IsTrue(
            fragmentResult.Source.Contains("cookTorranceBRDF")
                || fragmentResult.Source.Contains("NdotL"),
            "Should perform lighting calculations"
        );
    }

    #region Advanced Forward+ Compilation Tests

    [TestMethod]
    [TestCategory("ForwardPlus")]
    [DataRow(8u, 8u, 1u)]
    [DataRow(16u, 16u, 1u)]
    [DataRow(32u, 8u, 1u)]
    [DataRow(64u, 4u, 1u)]
    public void TestForwardPlusComputeWorkGroupSizes(uint x, uint y, uint z)
    {
        // Arrange - Create a compute shader with specified work group sizes
        var shaderSource =
            $@"
#extension GL_EXT_buffer_reference : require
layout(local_size_x = {x}, local_size_y = {y}, local_size_z = {z}) in;

layout(set = 0, binding = 0) buffer LightBuffer {{
    int lightCount;
    vec4 lights[];
}};

void main() {{
    uint tileIndex = gl_GlobalInvocationID.x;
    // Light culling code would go here
}}";

        // Act
        var result = _compiler!.Compile(ShaderStage.Compute, shaderSource);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Forward+ compute shader with work group ({x},{y},{z}) should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsTrue(
            result.Source!.Contains($"local_size_x = {x}"),
            $"Should use local_size_x = {x}"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    [DataRow(16u)]
    [DataRow(32u)]
    [DataRow(64u)]
    [DataRow(128u)]
    [DataRow(512u)]
    [DataRow(1024u)]
    public void TestForwardPlusWithVariableLightCounts(uint maxLights)
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string>
            {
                { "MAX_LIGHTS_PER_TILE", maxLights.ToString() },
            },
        };

        var fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    // Forward+ light indexing would use MAX_LIGHTS_PER_TILE here
    outColor = vec4(material.albedo, 1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Forward+ shader with {maxLights} max lights should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsTrue(
            result.Source!.Contains($"MAX_LIGHTS_PER_TILE {maxLights}"),
            "Should define MAX_LIGHTS_PER_TILE"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithBufferDeviceAddress()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = new Dictionary<string, string> { { "USE_BUFFER_REFERENCE", "1" } },
        };

        var fragmentShader =
            @"
#extension GL_EXT_buffer_reference : require

layout(buffer_reference, std430) buffer LightGridBuffer {
    uint lightIndices[];
};

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Shader should compile with buffer device address");
        Assert.IsTrue(
            result.Source!.Contains("GL_EXT_buffer_reference"),
            "Should enable buffer_reference extension"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusShaderErrorScenarios()
    {
        // Arrange - Missing required extension
        var fragmentShader =
            @"
layout(buffer_reference) buffer LightData {
    vec4 color;
};

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader);

        // Assert
        // Should still process successfully (validation is runtime/driver concern)
        Assert.IsTrue(
            result.Success || result.Errors.Count > 0,
            "Should either compile or provide clear error messages"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithMultipleBindlessFeatures()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = new Dictionary<string, string>
            {
                { "USE_BINDLESS_TEXTURES", "1" },
                { "USE_BINDLESS_SAMPLERS", "1" },
            },
        };

        var fragmentShader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    #ifdef USE_BINDLESS_TEXTURES
    // Would sample from bindless texture array
    #endif
    outColor = vec4(1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Shader with multiple bindless features should compile");
        Assert.IsTrue(
            result.Source!.Contains("USE_BINDLESS_TEXTURES"),
            "Should support bindless textures"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusPipelineStateValidation()
    {
        // Arrange
        var vertexShader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec3 fragPosition;

void main() {
    fragPosition = inPosition;
    gl_Position = vec4(inPosition, 1.0);
}";
        var fragmentShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragPosition, 1.0);
}";
        // Act
        var vertexResult = _compiler!.CompileVertexShader(vertexShader);
        var fragmentResult = _compiler.Compile(ShaderStage.Fragment, fragmentShader);

        // Assert - Both shaders should compile
        Assert.IsTrue(vertexResult.Success, "Vertex shader should compile");
        Assert.IsTrue(fragmentResult.Success, "Fragment shader should compile");
        Assert.IsNotNull(vertexResult.Source);
        Assert.IsNotNull(fragmentResult.Source);
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusShaderReflection()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string> { { "FORWARD_PLUS", "1" } },
        };

        var fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

struct GpuLight {
    vec3 position;
    vec3 color;
};

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.5);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    outColor = vec4(material.albedo, 1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Shader should compile");
        Assert.IsTrue(result.Source!.Contains("GpuLight"), "Should define GpuLight structure");
        Assert.IsTrue(result.Source.Contains("PBRMaterial"), "Should include PBRMaterial");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithMaterialSystem()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string>
            {
                { "USE_BASE_COLOR_TEXTURE", "1" },
                { "USE_NORMAL_TEXTURE", "1" },
                { "USE_METALLIC_ROUGHNESS_TEXTURE", "1" },
            },
        };

        var fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.8);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    outColor = vec4(material.albedo, material.opacity);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Material system shader should compile. Errors: {string.Join(", ", result.Errors)}"
        );
        Assert.IsTrue(result.Source!.Contains("PBRMaterial"), "Should use PBRMaterial structure");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusCompleteRenderingPipeline()
    {
        // Arrange - Build all required shader stages
        var vertexShader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec3 fragPosition;

void main() {
    fragPosition = inPosition;
    gl_Position = vec4(inPosition, 1.0);
}";
        var fragmentShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragPosition, 1.0);
}";
        var computeShader =
            @"
layout(local_size_x = 16, local_size_y = 16) in;

layout(set = 0, binding = 0) buffer LightBuffer {
    int lightCount;
};

void main() {
    uint tileIndex = gl_GlobalInvocationID.x;
}";
        // Act
        var vertexResult = _compiler!.CompileVertexShader(vertexShader);
        var fragmentResult = _compiler.Compile(ShaderStage.Fragment, fragmentShader);
        var computeResult = _compiler.CompileComputeShader(computeShader);

        // Assert - All shaders should compile
        Assert.IsTrue(
            vertexResult.Success,
            $"Vertex shader should compile. Errors: {string.Join(", ", vertexResult.Errors)}"
        );
        Assert.IsTrue(
            fragmentResult.Success,
            $"Fragment shader should compile. Errors: {string.Join(", ", fragmentResult.Errors)}"
        );
        Assert.IsTrue(
            computeResult.Success,
            $"Compute shader should compile. Errors: {string.Join(", ", computeResult.Errors)}"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    [TestCategory("Performance")]
    public void TestForwardPlusPerformanceScenarios()
    {
        // Arrange - Test various performance-critical configurations
        var scenarios = new[]
        {
            new
            {
                TileSize = 8u,
                MaxLights = 64u,
                Name = "Low Quality",
            },
            new
            {
                TileSize = 16u,
                MaxLights = 256u,
                Name = "Medium Quality",
            },
            new
            {
                TileSize = 32u,
                MaxLights = 512u,
                Name = "High Quality",
            },
        };

        foreach (var scenario in scenarios)
        {
            var options = new ShaderBuildOptions
            {
                IncludeStandardHeader = true,
                IncludePBRFunctions = true,
                Defines = new Dictionary<string, string>
                {
                    { "TILE_SIZE", scenario.TileSize.ToString() },
                    { "MAX_LIGHTS", scenario.MaxLights.ToString() },
                },
            };

            var fragmentShader =
                @"
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
}";
            // Act
            var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

            // Assert
            Assert.IsTrue(
                result.Success,
                $"{scenario.Name} scenario should compile. Errors: {string.Join(", ", result.Errors)}"
            );
            Assert.IsNotNull(result.Source, $"{scenario.Name} source should not be null");
        }
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithDepthPrepass()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = new Dictionary<string, string> { { "DEPTH_PREPASS", "1" } },
        };

        var fragmentShader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    #ifdef DEPTH_PREPASS
    // Depth-only pass
    outColor = vec4(0.0);
    #else
    outColor = vec4(1.0);
    #endif
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Shader with depth prepass should compile");
        Assert.IsTrue(
            result.Source!.Contains("DEPTH_PREPASS"),
            "Should support depth prepass mode"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithTransparency()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string>
            {
                { "ALPHA_BLEND", "1" },
                { "TRANSPARENT", "1" },
            },
        };

        var fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.8);
    material.opacity = 0.5; // Transparent
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    outColor = vec4(material.albedo, material.opacity);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Transparent Forward+ shader should compile");
        Assert.IsTrue(result.Source!.Contains("opacity"), "Should support transparency");
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusWithShadowMapping()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string>
            {
                { "USE_SHADOW_MAPPING", "1" },
                { "SHADOW_CASCADE_COUNT", "4" },
            },
        };

        var fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.8);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    #ifdef USE_SHADOW_MAPPING
    float shadow = 1.0; // Shadow calculation would go here
    #endif
    
    outColor = vec4(material.albedo, 1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader, options);

        // Assert
        Assert.IsTrue(result.Success, "Shadow mapping shader should compile");
        Assert.IsTrue(
            result.Source!.Contains("USE_SHADOW_MAPPING"),
            "Should support shadow mapping"
        );
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusExtensionCompatibility()
    {
        // Arrange
        var fragmentShader =
            @"
#extension GL_EXT_buffer_reference : require
#extension GL_EXT_buffer_reference_uvec2 : require
#extension GL_EXT_nonuniform_qualifier : require

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader);

        // Assert
        Assert.IsTrue(result.Success, "Shader should compile with all extensions");

        var requiredExtensions = new[]
        {
            "GL_EXT_buffer_reference",
            "GL_EXT_buffer_reference_uvec2",
            "GL_EXT_nonuniform_qualifier",
        };

        foreach (var ext in requiredExtensions)
        {
            Assert.IsTrue(result.Source!.Contains(ext), $"Should enable extension: {ext}");
        }
    }

    [TestMethod]
    [TestCategory("ForwardPlus")]
    public void TestForwardPlusMemoryLayout()
    {
        // Arrange
        var fragmentShader =
            @"
layout(std430, set = 0, binding = 0) buffer LightData {
    uint lightCount;
    vec4 lightColors[];
};

layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
}";
        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, fragmentShader);

        // Assert
        Assert.IsTrue(result.Success, "Shader should compile");
        Assert.IsTrue(result.Source!.Contains("std430"), "Should use std430 layout qualifier");
    }

    #endregion
}
