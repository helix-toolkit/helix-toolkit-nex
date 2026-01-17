using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Unit tests for the shader building system
/// </summary>
[TestClass]
public class ShaderBuildingTests
{
    private ShaderCompiler? _compiler;

    [TestInitialize]
    public void Initialize()
    {
        _compiler = new ShaderCompiler();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _compiler?.ClearCache();
        _compiler = null;
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    public void TestBasicCompilation()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
        };

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader, options);

        // Assert
        Assert.IsTrue(result.Success, "Compilation should succeed");
        Assert.IsNotNull(result.Source, "Source should not be null");
        Assert.AreEqual(
            0,
            result.Errors.Count,
            $"Should have no errors, but got: {string.Join(", ", result.Errors)}"
        );
        Assert.IsTrue(result.Source.Contains("#version"), "Should contain version directive");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("PBR")]
    public void TestPBRInclusion()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(1.0);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    outColor = vec4(material.albedo, 1.0);
}";

        // Act
        var result = _compiler!.CompileFragmentShaderWithPBR(shader);

        // Assert
        Assert.IsTrue(result.Success, "Compilation should succeed");
        Assert.IsNotNull(result.Source, "Source should not be null");
        Assert.IsTrue(
            result.Source.Contains("struct PBRMaterial"),
            "Should contain PBRMaterial struct definition"
        );
        CollectionAssert.Contains(
            result.IncludedFiles,
            "PBRFunctions.glsl",
            "Should include PBRFunctions.glsl"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Preprocessor")]
    public void TestDefines()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    #ifdef USE_RED
        outColor = vec4(1.0, 0.0, 0.0, 1.0);
    #else
        outColor = vec4(0.0, 1.0, 0.0, 1.0);
    #endif
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = new Dictionary<string, string> { { "USE_RED", "" } },
        };

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader, options);

        // Assert
        Assert.IsTrue(result.Success, "Compilation should succeed");
        Assert.IsNotNull(result.Source, "Source should not be null");
        Assert.IsTrue(
            result.Source.Contains("#define USE_RED"),
            "Should contain the USE_RED define"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Preprocessor")]
    public void TestDefineWithValue()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(MAX_VALUE / 100.0);
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = new Dictionary<string, string> { { "MAX_VALUE", "100" } },
        };

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader, options);

        // Assert
        Assert.IsTrue(result.Success, "Compilation should succeed");
        Assert.IsTrue(
            result.Source!.Contains("#define MAX_VALUE 100"),
            "Should contain the MAX_VALUE define with value"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Caching")]
    public void TestCaching()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
        };

        var cache = new ShaderCache(maxEntries: 10);
        var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

        // Act
        var result1 = compiler.Compile(ShaderStage.Fragment, shader, options);
        int count1 = cache.Count;

        var result2 = compiler.Compile(ShaderStage.Fragment, shader, options);
        int count2 = cache.Count;

        // Assert
        Assert.IsTrue(
            result1.Success,
            $"First compilation should succeed. Errors: {string.Join(", ", result1.Errors)}"
        );
        Assert.IsTrue(
            result2.Success,
            $"Second compilation should succeed. Errors: {string.Join(", ", result2.Errors)}"
        );
        Assert.AreEqual(1, count1, "Cache should have 1 entry after first compile");
        Assert.AreEqual(
            1,
            count2,
            "Cache should still have 1 entry after second compile (cache hit)"
        );

        var stats = cache.GetStatistics();
        Assert.AreEqual(1, stats.TotalEntries, "Should have 1 cached entry");
        Assert.IsTrue(
            stats.TotalAccessCount >= 2,
            $"Should have at least 2 accesses (1 initial + 1 cache hit), got {stats.TotalAccessCount}"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Caching")]
    public void TestCacheEviction()
    {
        // Arrange
        var cache = new ShaderCache(maxEntries: 2);
        var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
        };

        // Act - Compile 3 different shaders
        compiler.Compile(ShaderStage.Fragment, "void main() { }", options);
        compiler.Compile(ShaderStage.Fragment, "void main() { gl_FragCoord; }", options);
        compiler.Compile(ShaderStage.Fragment, "void main() { discard; }", options);

        // Assert
        Assert.AreEqual(2, cache.Count, "Cache should only contain 2 entries (LRU eviction)");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    public void TestMultipleStages()
    {
        // Arrange
        string vertexShader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec3 fragPosition;

void main() {
    fragPosition = inPosition;
    gl_Position = vec4(inPosition, 1.0);
}";

        string fragmentShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragPosition, 1.0);
}";

        // Act
        var vsResult = _compiler!.CompileVertexShader(vertexShader);
        var fsResult = _compiler.Compile(ShaderStage.Fragment, fragmentShader);

        // Assert
        Assert.IsTrue(vsResult.Success, "Vertex shader compilation should succeed");
        Assert.IsTrue(fsResult.Success, "Fragment shader compilation should succeed");
        Assert.IsNotNull(vsResult.Source, "Vertex shader source should not be null");
        Assert.IsNotNull(fsResult.Source, "Fragment shader source should not be null");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("FluentAPI")]
    public void TestFluentBuilder()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        // Act
        var result = GlslHeaders
            .BuildShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(shader)
            .WithStandardHeader()
            .WithDefine("TEST_DEFINE")
            .Build();

        // Assert
        Assert.IsTrue(result.Success, "Fluent builder should succeed");
        Assert.IsNotNull(result.Source, "Source should not be null");
        Assert.IsTrue(result.Source.Contains("#define TEST_DEFINE"), "Should contain TEST_DEFINE");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("FluentAPI")]
    public void TestFluentBuilderWithPBR()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(1.0);
    outColor = vec4(material.albedo, 1.0);
}";

        // Act
        var result = GlslHeaders
            .BuildShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(shader)
            .WithStandardHeader()
            .WithPBRFunctions()
            .Build();

        // Assert
        Assert.IsTrue(result.Success, "Should compile successfully");
        Assert.IsTrue(result.Source!.Contains("PBRMaterial"), "Should include PBR material struct");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Preprocessor")]
    public void TestCommentStripping()
    {
        // Arrange
        string shader =
            @"
// This is a comment
layout(location = 0) out vec4 outColor;
/* Multi-line
   comment */
void main() {
    outColor = vec4(1.0); // Inline comment
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            StripComments = true,
        };

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader, options);

        // Assert
        Assert.IsTrue(result.Success, "Should compile successfully");
        Assert.IsFalse(
            result.Source!.Contains("// This is a comment"),
            "Single-line comments should be stripped"
        );
        Assert.IsFalse(
            result.Source.Contains("/* Multi-line"),
            "Multi-line comments should be stripped"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    public void TestVersionDirectiveHandling()
    {
        // Arrange
        string shader =
            @"
#version 450
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader);

        // Assert
        Assert.IsTrue(result.Success, "Should compile successfully");
        Assert.IsTrue(
            result.Source!.StartsWith("#version 450"),
            "Version directive should be at the start"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    public void TestDefaultVersionDirective()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader);

        // Assert
        Assert.IsTrue(result.Success, "Should compile successfully");
        Assert.IsTrue(result.Source!.Contains("#version 460"), "Should default to version 460");
        Assert.AreEqual(
            1,
            result.Warnings.Count,
            "Should have warning about missing version directive"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("PBR")]
    public void TestPBRMaterialStructure()
    {
        // Arrange
        string shader =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial mat;
    mat.albedo = vec3(0.5);
    mat.metallic = 0.0;
    mat.roughness = 0.5;
    mat.ao = 1.0;
    mat.opacity = 1.0;
    mat.emissive = vec3(0.0);
    mat.normal = vec3(0.0, 0.0, 1.0);
    
    outColor = vec4(mat.albedo, mat.opacity);
}";

        // Act
        var result = _compiler!.CompileFragmentShaderWithPBR(shader);

        // Assert
        Assert.IsTrue(result.Success, "Should compile with PBR material");
        Assert.IsTrue(
            result.Source!.Contains("struct PBRMaterial"),
            "Should include PBRMaterial definition"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    public void TestAllShaderStages()
    {
        // Arrange
        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
        };

        var stages = new[]
        {
            (ShaderStage.Vertex, "CompileVertexShader"),
            (ShaderStage.Fragment, "Compile"),
            (ShaderStage.Compute, "CompileComputeShader"),
            (ShaderStage.Geometry, "CompileGeometryShader"),
            (ShaderStage.TessellationControl, "CompileTessControlShader"),
            (ShaderStage.TessellationEvaluation, "CompileTessEvalShader"),
            (ShaderStage.Mesh, "CompileMeshShader"),
            (ShaderStage.Task, "CompileTaskShader"),
        };

        // Act & Assert
        foreach (var (stage, methodName) in stages)
        {
            string shader = "void main() { }";
            var result = _compiler!.Compile(stage, shader, options);

            Assert.IsTrue(
                result.Success,
                $"{stage} compilation should succeed. Errors: {string.Join(", ", result.Errors)}"
            );
            Assert.IsNotNull(result.Source, $"{stage} source should not be null");
        }
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("ErrorHandling")]
    public void TestEmptyShaderSource()
    {
        // Arrange
        string shader = "";

        // Act
        var result = _compiler!.Compile(ShaderStage.Fragment, shader);

        // Assert
        Assert.IsTrue(result.Success, "Empty shader should still process successfully");
        Assert.IsNotNull(result.Source, "Should produce output even for empty shader");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Caching")]
    public void TestCacheStatistics()
    {
        // Arrange
        var cache = new ShaderCache(maxEntries: 10);
        var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);
        string shader = "void main() { }";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
        };

        // Act
        compiler.Compile(ShaderStage.Fragment, shader, options);
        compiler.Compile(ShaderStage.Fragment, shader, options); // Cache hit
        var stats = cache.GetStatistics();

        // Assert
        Assert.AreEqual(1, stats.TotalEntries, "Should have 1 entry");
        // Cache hit increments access count, so we should have at least 2 (initial + 2 hits)
        // Note: The exact count depends on internal implementation details, but should be >= 2
        Assert.IsTrue(
            stats.TotalAccessCount >= 2,
            $"Should have at least 2 accesses (1 initial + 1 cache hit), got {stats.TotalAccessCount}"
        );
        Assert.IsTrue(
            stats.AverageAccessCount >= 2,
            $"Average should be at least 2, got {stats.AverageAccessCount}"
        );
        Assert.IsNotNull(stats.OldestEntry, "Should have oldest entry timestamp");
        Assert.IsNotNull(stats.NewestEntry, "Should have newest entry timestamp");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Factory")]
    public void TestFactoryMethods()
    {
        // Act
        var compiler1 = GlslHeaders.CreateCompiler();
        var compiler2 = GlslHeaders.CreateCompiler(useGlobalCache: false);
        var builder = GlslHeaders.BuildShader();

        // Assert
        Assert.IsNotNull(compiler1, "CreateCompiler should return valid compiler");
        Assert.IsNotNull(compiler2, "CreateCompiler with parameter should return valid compiler");
        Assert.IsNotNull(builder, "BuildShader should return valid builder");
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Headers")]
    public void TestGetHeaderDirectly()
    {
        // Act
        var fragmentHeader = GlslHeaders.GetShaderHeader(ShaderStage.Fragment);
        var vertexHeader = GlslHeaders.GetShaderHeader(ShaderStage.Vertex);
        var pbrFunctions = GlslHeaders.GetGlslShaderPBRFunction();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(fragmentHeader), "Fragment header should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(vertexHeader), "Vertex header should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(pbrFunctions), "PBR functions should not be empty");
        Assert.IsTrue(
            pbrFunctions.Contains("struct PBRMaterial"),
            "PBR functions should contain PBRMaterial"
        );
    }

    [TestMethod]
    [TestCategory("ShaderBuilding")]
    [TestCategory("Includes")]
    public void TestRelativePathInclude()
    {
        // This test specifically checks if relative paths like "../Headers/HeaderFrag.glsl" 
        // are correctly resolved to the embedded resource path logic in ShaderBuilder.cs

        string shader = @"
#version 460
#include ""../Headers/HeaderFrag.glsl""
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = false, // We're manually including it
            IncludePBRFunctions = false
        };

        var result = _compiler!.Compile(ShaderStage.Fragment, shader, options);

        Assert.IsTrue(result.Success, $"Compilation failed: {string.Join(", ", result.Errors)}");
        Assert.IsTrue(result.Source!.Contains("// Begin include: ../Headers/HeaderFrag.glsl"),
            "Should verify include directive was processed");
    }
}
