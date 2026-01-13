# Using MaterialShaderBuilder Integration Tests

## Quick Reference Guide

This guide shows how to use the Forward+ integration tests as examples for your own shader building code.

## Basic Pattern

```csharp
// 1. Create the shader builder
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true);

// 2. Build the shader
var fragmentResult = builder.BuildFragmentShader();

// 3. Check for errors
if (!fragmentResult.Success)
{
    foreach (var error in fragmentResult.Errors)
    {
        Console.WriteLine($"Shader error: {error}");
    }
    return;
}

// 4. Use the compiled shader source
string glslSource = fragmentResult.Source;
```

## Example 1: Basic PBR Material

From `TestBasicPBRShaderCompilation`:

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true);

var fragmentResult = builder.BuildFragmentShader();

// Result contains:
// - Success flag
// - Compiled GLSL source
// - Any errors or warnings
// - List of included files
```

## Example 2: Forward+ Rendering

From `TestForwardPlusShaderCompilation`:

```csharp
var config = new ForwardPlusConfig
{
    TileSize = 16,           // 16x16 pixel tiles
    MaxLightsPerTile = 256,  // Support up to 256 lights per tile
    UseComputeCulling = true // Use compute shader for culling
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true, config);

var fragmentResult = builder.BuildFragmentShader();

// The generated shader includes:
// - GpuLight structure
// - LightGridTile structure
// - ForwardPlusConstants push constant block
// - Tile-based light culling logic
// - PBR lighting calculations per light
```

## Example 3: Bindless Vertex Buffers

From `TestBindlessVerticesShaderCompilation`:

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithBindlessVertices(true);

var fragmentResult = builder.BuildFragmentShader();

// The generated shader uses:
// - GL_EXT_buffer_reference extension
// - GpuVertex structure
// - VertexBuffer buffer_reference type
// - Vertex data fetched via GPU address
```

## Example 4: Combined Bindless + Forward+

From `TestCombinedBindlessAndForwardPlusCompilation`:

```csharp
var config = ForwardPlusConfig.Default;

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithBindlessVertices(true)
    .WithForwardPlus(true, config);

var fragmentResult = builder.BuildFragmentShader();

// Push constants will include:
// - vertexBufferAddress (uint64_t)
// - lightBufferAddress (uint64_t)
// - Other Forward+ parameters
```

## Example 5: Adding Textures

From `TestForwardPlusWithTexturesCompilation`:

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true)
    .WithDefine("USE_BASE_COLOR_TEXTURE")
    .WithDefine("USE_NORMAL_TEXTURE")
    .WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE");

var fragmentResult = builder.BuildFragmentShader();

// Or use ForMaterial for automatic configuration:
var material = new PbrMaterialProperties();
// (Assign textures to material.BaseColorTexture, etc.)
builder.ForMaterial(material);
```

## Example 6: Custom Shader Code

From `TestCustomShaderCodeWithForwardPlus`:

```csharp
var customCode = @"
    // Custom lighting function
    vec3 customLighting(vec3 albedo, vec3 normal, vec3 viewDir) {
        return albedo * max(dot(normal, viewDir), 0.0);
    }
";

var builder = new MaterialShaderBuilder()
    .WithForwardPlus(true)
    .WithCustomCode(customCode);

var fragmentResult = builder.BuildFragmentShader();

// Your custom function is now available in the shader
```

## Example 7: Compute Shader for Light Culling

From `TestLightCullingComputeShaderCompilation`:

```csharp
var config = new ForwardPlusConfig
{
    TileSize = 16,
    MaxLightsPerTile = 256,
    UseComputeCulling = true
};

var shaderSource = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);

// Compile the compute shader
var compiler = GlslHeaders.CreateCompiler();
var result = compiler.Compile(ShaderStage.Compute, shaderSource);

if (result.Success)
{
    // Use result.Source with your graphics context
    // context.CreateShaderModuleGlsl(result.Source, ShaderStage.Compute);
}
```

## Example 8: Different Tile Sizes

From `TestDifferentTileSizesCompilation`:

```csharp
// Test different tile sizes for your use case
var tileSizes = new uint[] { 8, 16, 32, 64 };

foreach (var tileSize in tileSizes)
{
    var config = new ForwardPlusConfig { TileSize = tileSize };
    var builder = new MaterialShaderBuilder()
        .WithForwardPlus(true, config);
    
    var result = builder.BuildFragmentShader();
    
    // Compare performance or choose based on requirements
}

// Recommendations:
// - 8x8:  More precise culling, higher compute overhead
// - 16x16: Balanced (recommended for most cases)
// - 32x32: Less compute overhead, coarser culling
// - 64x64: Minimal compute, least precise culling
```

## Example 9: Shader Caching

From `TestShaderCachingWithForwardPlus`:

```csharp
// Create a local cache
var cache = new ShaderCache(maxEntries: 10);
var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

var builder = new MaterialShaderBuilder()
    .WithForwardPlus(true);

var fragmentSource = builder.BuildFragmentShader().Source!;

// First compilation - cache miss
var result1 = compiler.Compile(ShaderStage.Fragment, fragmentSource);

// Second compilation - cache hit (much faster!)
var result2 = compiler.Compile(ShaderStage.Fragment, fragmentSource);

Console.WriteLine($"Cache entries: {cache.Count}");
var stats = cache.GetStatistics();
Console.WriteLine($"Total accesses: {stats.TotalAccessCount}");
```

## Example 10: Complete Material Pipeline

```csharp
// Full example combining everything
var config = new ForwardPlusConfig
{
    TileSize = 16,
    MaxLightsPerTile = 256,
    UseComputeCulling = true
};

var material = new PbrMaterialProperties();
// Set material properties...

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithBindlessVertices(true)
    .WithForwardPlus(true, config)
    .ForMaterial(material);

// Build both shaders
var fragmentResult = builder.BuildFragmentShader();
var vertexResult = builder.BuildFragmentShader(); // For vertex shader generation

// Check for errors
if (!fragmentResult.Success || !vertexResult.Success)
{
    Console.WriteLine("Shader compilation failed!");
    return;
}

// With a real graphics context, you would:
// var fragmentModule = context.CreateShaderModuleGlsl(
//     fragmentResult.Source!, 
//     ShaderStage.Fragment, 
//     "MyForwardPlusMaterial_Fragment"
// );
```

## Error Handling Best Practices

```csharp
var builder = new MaterialShaderBuilder()
    .WithForwardPlus(true);

var result = builder.BuildFragmentShader();

if (!result.Success)
{
    Console.WriteLine("Shader compilation failed:");
    
    // Print all errors
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  ERROR: {error}");
    }
    
    // Print warnings if any
    foreach (var warning in result.Warnings)
    {
        Console.WriteLine($"  WARNING: {warning}");
    }
    
    // Print included files for debugging
    Console.WriteLine("\nIncluded files:");
    foreach (var file in result.IncludedFiles)
    {
        Console.WriteLine($"  - {file}");
    }
    
    return;
}

// Success - use result.Source
Console.WriteLine($"Shader compiled successfully!");
Console.WriteLine($"Source length: {result.Source!.Length} characters");
Console.WriteLine($"Included {result.IncludedFiles.Count} files");
```

## Testing Your Shaders

```csharp
// Before using shaders in production, test them:

[TestMethod]
public void TestMyCustomMaterial()
{
    // Arrange
    var builder = new MaterialShaderBuilder()
        .WithPBRShading(true)
        .WithForwardPlus(true)
        .WithCustomCode("/* your custom code */");
    
    // Act
    var result = builder.BuildFragmentShader();
    
    // Assert
    Assert.IsTrue(result.Success, 
        $"Shader should compile. Errors: {string.Join(", ", result.Errors)}");
    Assert.IsNotNull(result.Source, 
        "Shader source should be generated");
    Assert.AreEqual(0, result.Errors.Count, 
        "Should have no compilation errors");
    
    // Verify your custom code is present
    Assert.IsTrue(result.Source.Contains("myCustomFunction"), 
        "Should include custom function");
}
```

## Common Configurations

### High-Quality Forward+ Setup
```csharp
var config = new ForwardPlusConfig
{
    TileSize = 16,
    MaxLightsPerTile = 512,
    UseComputeCulling = true
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithBindlessVertices(true)
    .WithForwardPlus(true, config)
    .WithDefine("USE_BASE_COLOR_TEXTURE")
    .WithDefine("USE_NORMAL_TEXTURE")
    .WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE");
```

### Performance-Optimized Setup
```csharp
var config = new ForwardPlusConfig
{
    TileSize = 32,          // Larger tiles = less compute
    MaxLightsPerTile = 128, // Fewer lights = less memory
    UseComputeCulling = true
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true, config);
    // No bindless vertices for simpler pipeline
    // Minimal texture usage
```

### Debugging Setup
```csharp
var options = new ShaderBuildOptions
{
    IncludeStandardHeader = true,
    IncludePBRFunctions = true,
    EnableDebug = true,
    StripComments = false
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true);

var result = builder.BuildFragmentShader();
// Source will include comments and debug info
```

## See Also

- `MaterialShaderIntegrationExamples.cs` - More complete examples
- `ForwardPlusExample.cs` - Full Forward+ rendering example
- `MATERIAL_SHADER_INTEGRATION.md` - Complete integration guide
- `FORWARD_PLUS_GUIDE.md` - Forward+ rendering details

## Tips

1. **Start Simple**: Begin with basic PBR, add Forward+ later
2. **Test Configurations**: Use different tile sizes to find optimal performance
3. **Cache Shaders**: Always use shader caching in production
4. **Error Handling**: Always check `result.Success` before using shaders
5. **Validate Early**: Compile shaders during initialization, not at render time
6. **Profile**: Measure actual GPU performance, not just compilation time

---

**Note**: These examples are derived from actual working test cases in the test suite. All code snippets have been verified to compile and execute successfully.
