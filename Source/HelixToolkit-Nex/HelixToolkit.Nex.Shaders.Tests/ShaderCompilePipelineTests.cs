using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Integration tests for the complete shader compile pipeline (preprocessing + SPIR-V compilation)
/// These tests require GPU context and test the full workflow
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
[TestCategory("Integration")]
public class ShaderCompilePipelineTests
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

    #region Basic Pipeline Tests

    [TestMethod]
    public void TestBasicShaderCompilePipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0, 0.0, 0.0, 1.0);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            debugName: "TestBasicShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsNotNull(buildResult.Source, "Processed source should not be null");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid");
        Assert.AreEqual(0, buildResult.Errors.Count, "Should have no errors");
    }

    [TestMethod]
    public void TestPBRShaderCompilePipeline()
    {
        // Arrange
        string shader =
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
    
    outColor = vec4(material.albedo, material.opacity);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileFragmentShaderWithPBR(
            shader,
            debugName: "TestPBRShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid");
        Assert.IsTrue(
            buildResult.Source!.Contains("struct PBRMaterial"),
            "Should include PBR material"
        );
        CollectionAssert.Contains(buildResult.IncludedFiles, "PBRFunctions.glsl");
    }

    [TestMethod]
    public void TestVertexShaderCompilePipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 0) out vec3 fragPosition;
layout(location = 1) out vec3 fragNormal;

layout(push_constant) uniform PushConstants {
    mat4 mvp;
} pc;

void main() {
    fragPosition = inPosition;
    fragNormal = inNormal;
    gl_Position = pc.mvp * vec4(inPosition, 1.0);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileVertexShader(
            shader,
            debugName: "TestVertexShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid");
        Assert.IsNotNull(buildResult.Source);
    }

    #endregion

    #region Fluent Builder Pipeline Tests

    [TestMethod]
    public void TestFluentBuilderPipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    #ifdef USE_COLOR_GRADIENT
        outColor = vec4(fragPosition, 1.0);
    #else
        outColor = vec4(1.0);
    #endif
}";

        // Act
        var (buildResult, shaderModule) = _context!
            .BuildAndCompileShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(shader)
            .WithStandardHeader()
            .WithDefine("USE_COLOR_GRADIENT")
            .WithDebugName("TestFluentBuilder")
            .Build();
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid");
        Assert.IsTrue(buildResult.Source!.Contains("#define USE_COLOR_GRADIENT"));
    }

    [TestMethod]
    public void TestFluentBuilderWithPBRPipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial mat;
    mat.albedo = vec3(1.0);
    mat.metallic = 0.0;
    mat.roughness = 0.5;
    mat.ao = 1.0;
    mat.opacity = 1.0;
    mat.emissive = vec3(0.0);
    mat.normal = normalize(fragNormal);
    
    outColor = vec4(mat.albedo, 1.0);
}";

        // Act
        var (buildResult, shaderModule) = _context!
            .BuildAndCompileShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(shader)
            .WithStandardHeader()
            .WithPBRFunctions()
            .WithDebugName("TestFluentPBR")
            .Build();
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid");
        Assert.IsTrue(buildResult.Source!.Contains("PBRMaterial"));
    }

    #endregion

    #region Multiple Shader Stages Pipeline

    [TestMethod]
    public void TestMultipleShaderStagesPipeline()
    {
        // Arrange
        string vertexShader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inTexCoord;
layout(location = 0) out vec2 fragTexCoord;

void main() {
    fragTexCoord = inTexCoord;
    gl_Position = vec4(inPosition, 1.0);
}";

        string fragmentShader =
            @"
layout(location = 0) in vec2 fragTexCoord;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragTexCoord, 0.0, 1.0);
}";

        // Act
        var (vsBuild, vsModule) = _context!.BuildAndCompileVertexShader(
            vertexShader,
            debugName: "TestMultiStage_Vertex"
        );
        var (fsBuild, fsModule) = _context.BuildAndCompileShader(
            ShaderStage.Fragment,
            fragmentShader,
            debugName: "TestMultiStage_Fragment"
        );
        using var vs = vsModule;
        using var fs = fsModule;
        // Assert
        Assert.IsTrue(vsBuild.Success, "Vertex build should succeed");
        Assert.IsTrue(vsModule.Valid, "Vertex module should be valid");
        Assert.IsTrue(fsBuild.Success, "Fragment build should succeed");
        Assert.IsTrue(fsModule.Valid, "Fragment module should be valid");
    }

    [TestMethod]
    public void TestComputeShaderPipeline()
    {
        // Arrange
        string shader =
            @"
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba8) uniform image2D outputImage;

void main() {
    ivec2 coords = ivec2(gl_GlobalInvocationID.xy);
    vec4 color = vec4(float(coords.x) / 256.0, float(coords.y) / 256.0, 0.0, 1.0);
    imageStore(outputImage, coords, color);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Compute,
            shader,
            new ShaderBuildOptions { IncludeStandardHeader = true },
            "TestComputeShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Compute build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Compute module should be valid");
    }

    #endregion

    #region Advanced Features Pipeline Tests

    [TestMethod]
    public void TestShaderWithMultipleDefinesPipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    vec4 color = vec4(0.0);
    
    #ifdef FEATURE_A
        color.r = 1.0;
    #endif
    
    #ifdef FEATURE_B
        color.g = 1.0;
    #endif
    
    #ifdef FEATURE_C
        color.b = 1.0;
    #endif
    
    color.a = 1.0;
    outColor = color;
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            Defines = new Dictionary<string, string>
            {
                { "FEATURE_A", "" },
                { "FEATURE_B", "" },
                { "FEATURE_C", "" },
            },
        };

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            options,
            "TestMultipleDefines"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
        Assert.IsTrue(buildResult.Source!.Contains("#define FEATURE_A"));
        Assert.IsTrue(buildResult.Source.Contains("#define FEATURE_B"));
        Assert.IsTrue(buildResult.Source.Contains("#define FEATURE_C"));
    }

    [TestMethod]
    public void TestShaderWithPushConstantsPipeline()
    {
        // Arrange
        string shader =
            @"
layout(push_constant) uniform PushConstants {
    mat4 modelMatrix;
    mat4 viewMatrix;
    mat4 projMatrix;
    vec4 color;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec4 fragColor;

void main() {
    gl_Position = pc.projMatrix * pc.viewMatrix * pc.modelMatrix * vec4(inPosition, 1.0);
    fragColor = pc.color;
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileVertexShader(
            shader,
            debugName: "TestPushConstants"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
        Assert.IsTrue(buildResult.Source!.Contains("push_constant"));
    }

    [TestMethod]
    public void TestShaderWithTexturesPipeline()
    {
        // Arrange
        string shader =
            @"
#extension GL_EXT_nonuniform_qualifier : require
layout(location = 0) in vec2 fragTexCoord;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

layout(push_constant) uniform PushConstants {
    uint textureIndex;
    uint samplerIndex;
} pc;

void main() {
    outColor = texture(sampler2D(kTextures2D[pc.textureIndex], kSamplers[pc.samplerIndex]), fragTexCoord);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            new ShaderBuildOptions { IncludeStandardHeader = false }, // Already has bindless texture declarations
            "TestTextures"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
    }

    #endregion

    #region Error Handling Pipeline Tests

    [TestMethod]
    public void TestInvalidShaderSyntaxPipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    // Missing semicolon
    outColor = vec4(1.0, 0.0, 0.0, 1.0)
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            debugName: "TestInvalidSyntax"
        );
        using var module = shaderModule;
        // Assert
        // Preprocessing should succeed, but SPIR-V compilation should fail
        Assert.IsTrue(buildResult.Success, "Preprocessing should succeed");
        Assert.IsFalse(shaderModule.Valid, "Module should be invalid due to compilation error");
    }

    [TestMethod]
    public void TestMissingPBRInclusionPipeline()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    // PBRMaterial is not included, this should fail
    PBRMaterial mat;
    outColor = vec4(1.0);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            new ShaderBuildOptions { IncludePBRFunctions = false },
            "TestMissingPBR"
        );
        using var module = shaderModule;
        // Assert
        // Preprocessing succeeds, SPIR-V compilation fails
        Assert.IsTrue(buildResult.Success, "Preprocessing should succeed");
        Assert.IsFalse(shaderModule.Valid, "Module should be invalid (undefined PBRMaterial)");
    }

    [TestMethod]
    public void TestEmptyShaderPipeline()
    {
        // Arrange
        string shader = "";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            debugName: "TestEmptyShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Should success on empty shader");
        // Empty shader compilation result depends on implementation
        // At minimum, it should have headers
        Assert.IsNotNull(buildResult.Source);
        Assert.IsFalse(module.Valid, "Should failed to compile empty shader.");
    }

    #endregion

    #region Performance and Stress Tests

    [TestMethod]
    public void TestBatchShaderCompilationPipeline()
    {
        // Arrange
        var shaderSources = new Dictionary<string, string>
        {
            ["Simple"] =
                "layout(location = 0) out vec4 outColor; void main() { outColor = vec4(1.0); }",
            ["WithInput"] =
                "layout(location = 0) in vec3 fragPos; layout(location = 0) out vec4 outColor; void main() { outColor = vec4(fragPos, 1.0); }",
            ["WithTexture"] =
                "layout(location = 0) in vec2 uv; layout(location = 0) out vec4 outColor; void main() { outColor = vec4(uv, 0.0, 1.0); }",
        };

        var compiledModules = new Dictionary<string, ShaderModuleResource>();
        var errors = new List<string>();

        // Act
        foreach (var (name, source) in shaderSources)
        {
            var (buildResult, module) = _context!.BuildAndCompileShader(
                ShaderStage.Fragment,
                source,
                debugName: $"Batch_{name}"
            );

            if (buildResult.Success && module.Valid)
            {
                compiledModules[name] = module;
            }
            else
            {
                errors.Add($"{name}: {string.Join("; ", buildResult.Errors)}");
            }
        }

        // Assert
        Assert.AreEqual(
            shaderSources.Count,
            compiledModules.Count,
            "All shaders should compile successfully"
        );
        Assert.AreEqual(
            0,
            errors.Count,
            $"Should have no errors, but got: {string.Join(", ", errors)}"
        );
        foreach (var module in compiledModules.Values)
        {
            module.Dispose();
        }
    }

    [TestMethod]
    public void TestLargeShaderPipeline()
    {
        // Arrange - Create a large shader with many functions
        var shaderBuilder = new System.Text.StringBuilder();
        shaderBuilder.AppendLine("layout(location = 0) out vec4 outColor;");
        shaderBuilder.AppendLine();

        // Add many helper functions
        for (int i = 0; i < 50; i++)
        {
            shaderBuilder.AppendLine($"float func{i}(float x) {{ return x * {i}.0; }}");
        }

        shaderBuilder.AppendLine();
        shaderBuilder.AppendLine("void main() {");
        shaderBuilder.AppendLine("    float result = 0.0;");
        for (int i = 0; i < 50; i++)
        {
            shaderBuilder.AppendLine($"    result += func{i}(1.0);");
        }
        shaderBuilder.AppendLine("    outColor = vec4(result / 1000.0);");
        shaderBuilder.AppendLine("}");

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shaderBuilder.ToString(),
            debugName: "TestLargeShader"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "Large shader build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Large shader module should be valid");
    }

    #endregion

    #region Real-World Scenario Tests

    [TestMethod]
    public void TestShaderToyStyleShaderPipeline()
    {
        // Arrange - ShaderToy-like shader
        string shader =
            @"
layout(push_constant) uniform Constants {
    vec3 iResolution;
    float iTime;
} pc;

layout(location = 0) in vec2 fragCoord;
layout(location = 0) out vec4 fragColor;

void mainImage(out vec4 fragColor, in vec2 fragCoord) {
    vec2 uv = fragCoord / pc.iResolution.xy;
    vec3 col = 0.5 + 0.5 * cos(pc.iTime + uv.xyx + vec3(0, 2, 4));
    fragColor = vec4(col, 1.0);
}

void main() {
    mainImage(fragColor, fragCoord * pc.iResolution.xy);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            debugName: "TestShaderToy"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "ShaderToy-style shader should compile");
        Assert.IsTrue(shaderModule.Valid, "ShaderToy module should be valid");
    }

    [TestMethod]
    public void TestPBRLightingShaderPipeline()
    {
        // Arrange - Full PBR lighting shader
        string shader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    vec3 lightDirection;
    vec3 lightColor;
    float lightIntensity;
} pc;

void main() {
    // Setup PBR material
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    // Calculate view direction
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    
    // Use PBR shading function
    vec3 color = pbrShadingSimple(
        material,
        pc.lightDirection,
        pc.lightColor,
        pc.lightIntensity,
        viewDir,
        vec3(0.03)
    );
    
    // Tone mapping
    color = color / (color + vec3(1.0));
    
    // Gamma correction
    color = pow(color, vec3(1.0/2.2));
    
    outColor = vec4(color, material.opacity);
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileFragmentShaderWithPBR(
            shader,
            debugName: "TestPBRLighting"
        );
        using var module = shaderModule;
        // Assert
        Assert.IsTrue(buildResult.Success, "PBR lighting shader should compile");
        Assert.IsTrue(shaderModule.Valid, "PBR lighting module should be valid");
        Assert.IsTrue(
            buildResult.Source!.Contains("pbrShadingSimple"),
            "Should include PBR shading function"
        );
    }

    #endregion

    #region NonUniform Qualifier Tests

    [TestMethod]
    public void TestShaderWithNonUniformTextureIndexingPipeline()
    {
        // Arrange - Shader using nonuniformEXT for dynamic texture array indexing
        string shader =
            @"
#extension GL_EXT_nonuniform_qualifier : require

layout(location = 0) in vec2 fragTexCoord;
layout(location = 1) flat in uint materialID;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

void main() {
    // Use nonuniformEXT to indicate the index can vary per invocation
    // This is required when the texture/sampler index is dynamically computed
    // and not uniform across the draw call
    uint texIndex = nonuniformEXT(materialID);
    uint samplerIndex = nonuniformEXT(materialID % 4);
    
    outColor = texture(
        sampler2D(kTextures2D[texIndex], kSamplers[samplerIndex]), 
        fragTexCoord
    );
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            new ShaderBuildOptions { IncludeStandardHeader = false },
            "TestNonUniformIndexing"
        );
        using var module = shaderModule;

        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
        Assert.IsTrue(
            buildResult.Source!.Contains("GL_EXT_nonuniform_qualifier"),
            "Should include nonuniform qualifier extension"
        );
    }

    [TestMethod]
    public void TestShaderWithNonUniformBufferIndexingPipeline()
    {
        // Arrange - Shader using nonuniformEXT for dynamic buffer array indexing
        string shader =
            @"
#extension GL_EXT_nonuniform_qualifier : require

layout(location = 0) in vec3 fragPosition;
layout(location = 1) flat in uint objectID;
layout(location = 0) out vec4 outColor;

// Array of uniform buffers containing material properties
layout(set = 0, binding = 0) uniform MaterialData {
    vec4 albedo;
    vec4 params; // metallic, roughness, ao, opacity
} materials[];

void main() {
    // Use nonuniformEXT for dynamic indexing into buffer arrays
    uint matIndex = nonuniformEXT(objectID);
    vec4 albedo = materials[matIndex].albedo;
    
    outColor = albedo;
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            new ShaderBuildOptions { IncludeStandardHeader = false },
            "TestNonUniformBufferIndexing"
        );
        using var module = shaderModule;

        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
    }

    [TestMethod]
    public void TestShaderWithoutNonUniformQualifierShouldStillCompile()
    {
        // Arrange - Shader without nonuniformEXT but with uniform indexing
        // This should still compile as long as the index is uniform across the draw
        string shader =
            @"
layout(location = 0) in vec2 fragTexCoord;
layout(location = 0) out vec4 outColor;
layout(constant_id = 0) const uint textureArraySize = 1;

layout(set = 0, binding = 0) uniform texture2D kTextures2D[textureArraySize];
layout(set = 0, binding = 1) uniform sampler kSamplers[textureArraySize];

layout(push_constant) uniform PushConstants {
    uint textureIndex;
    uint samplerIndex;
} pc;

void main() {
    // These indices are uniform (same for all invocations in this draw)
    // so nonuniformEXT is not required
    outColor = texture(
        sampler2D(kTextures2D[pc.textureIndex], kSamplers[pc.samplerIndex]), 
        fragTexCoord
    );
}";

        // Act
        var (buildResult, shaderModule) = _context!.BuildAndCompileShader(
            ShaderStage.Fragment,
            shader,
            new ShaderBuildOptions { IncludeStandardHeader = false },
            "TestUniformIndexing"
        );
        using var module = shaderModule;

        // Assert
        Assert.IsTrue(buildResult.Success, "Build should succeed");
        Assert.IsTrue(shaderModule.Valid, "Module should be valid");
    }

    #endregion

    #region Comparison: Two-Step vs One-Step Pipeline

    [TestMethod]
    public void TestComparisonTwoStepVsOneStep()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0, 0.0, 0.0, 1.0);
}";

        // Act - Two-step approach
        var compiler = new ShaderCompiler();
        var buildResult1 = compiler.CompileFragmentShaderWithPBR(shader);
        var module1 = ShaderModuleResource.Null;
        if (buildResult1.Success)
        {
            module1 = _context!.CreateShaderModuleGlsl(
                buildResult1.Source!,
                ShaderStage.Fragment,
                "TwoStep"
            );
        }
        using var m1 = module1;
        // Act - One-step approach
        var (buildResult2, module2) = _context!.BuildAndCompileFragmentShaderWithPBR(
            shader,
            debugName: "OneStep"
        );
        using var m2 = module2;
        // Assert - Both should produce equivalent results
        Assert.IsTrue(buildResult1.Success, "Two-step build should succeed");
        Assert.IsTrue(buildResult2.Success, "One-step build should succeed");
        Assert.IsTrue(module1.Valid, "Two-step module should be valid");
        Assert.IsTrue(module2.Valid, "One-step module should be valid");

        // Both approaches should include the same files
        CollectionAssert.AreEquivalent(
            buildResult1.IncludedFiles,
            buildResult2.IncludedFiles,
            "Both approaches should include the same files"
        );
    }

    #endregion
}
