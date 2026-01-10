# Shader Building System

The HelixToolkit.Nex.Shaders project provides a powerful shader building system that automatically includes necessary headers (HeaderFrag.glsl, PBRFunctions.glsl, etc.) for user-defined shaders.

## Features

- **Automatic Header Inclusion**: Automatically includes the correct header file based on shader stage
- **PBR Support**: Easy inclusion of PBR functions for physically-based rendering
- **Preprocessor Support**: Handle #include directives and custom defines
- **Shader Caching**: Built-in caching system with LRU eviction for performance
- **Fluent API**: Clean, readable builder pattern for shader compilation
- **Comment Stripping**: Optional removal of comments to reduce shader size
- **Thread-Safe**: All operations are thread-safe

## Quick Start

### Basic Usage

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

// Your custom fragment shader (no need to include headers manually)
string myShader = @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
} pc;

void main() {
    // Use PBR functions directly - they're automatically included!
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    
    vec3 color = pbrShadingSimple(material, lightDir, vec3(1.0), 3.0, viewDir, vec3(0.03));
    
    outColor = vec4(color, 1.0);
}";

// Compile with automatic header and PBR inclusion
var compiler = new ShaderCompiler();
var result = compiler.CompileFragmentShaderWithPBR(myShader);

if (result.Success)
{
    // Use result.Source as your final shader code
    string finalShader = result.Source;
    // Pass to your graphics API...
}
else
{
    // Handle errors
    foreach (var error in result.Errors)
    {
        Console.WriteLine(error);
    }
}
```

### Using the Fluent Builder API

```csharp
var result = GlslHeaders.BuildShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(myShader)
    .WithStandardHeader()
    .WithPBRFunctions()
    .WithDefine("USE_ADVANCED_LIGHTING")
    .WithDefine("MAX_LIGHTS", "8")
    .StripComments()
    .Build();
```

## API Reference

### ShaderCompiler

The main class for compiling shaders.

```csharp
// Create a compiler instance
var compiler = new ShaderCompiler(useGlobalCache: true);

// Compile for different stages
var result = compiler.CompileFragmentShaderWithPBR(source);
var result = compiler.CompileVertexShader(source);
var result = compiler.CompileComputeShader(source);
var result = compiler.CompileGeometryShader(source);
var result = compiler.CompileMeshShader(source);
var result = compiler.CompileTaskShader(source);

// Generic compile with custom options
var options = new ShaderBuildOptions { ... };
var result = compiler.Compile(ShaderStage.Fragment, source, options);
```

### ShaderBuildOptions

Configure how your shader is built:

```csharp
var options = new ShaderBuildOptions
{
    IncludeStandardHeader = true,    // Include HeaderFrag.glsl, HeaderVertex.glsl, etc.
    IncludePBRFunctions = true,      // Include PBRFunctions.glsl
    StripComments = false,           // Remove comments from output
    EnableDebug = false,             // Enable debug information
    Defines = new Dictionary<string, string>
    {
        { "USE_LIGHTING", "1" },
        { "MAX_LIGHTS", "4" }
    }
};
```

### ShaderBuildResult

Result of a shader compilation:

```csharp
public class ShaderBuildResult
{
    public bool Success { get; set; }              // Was compilation successful?
    public string? Source { get; set; }            // Final processed shader source
    public List<string> Errors { get; set; }       // Any errors
    public List<string> Warnings { get; set; }     // Any warnings
    public List<string> IncludedFiles { get; set; } // List of included files
}
```

### ShaderCache

Manage shader caching for improved performance:

```csharp
// Use the global cache (default)
var compiler = new ShaderCompiler(useGlobalCache: true);

// Create a local cache with custom settings
var localCache = new ShaderCache(
    maxEntries: 100,
    expirationTime: TimeSpan.FromMinutes(30)
);
var compiler = new ShaderCompiler(useGlobalCache: false, localCache: localCache);

// Get cache statistics
var stats = compiler.GetCacheStatistics();
Console.WriteLine($"Cached entries: {stats.TotalEntries}");
Console.WriteLine($"Average accesses: {stats.AverageAccessCount}");

// Clear cache
compiler.ClearCache();
```

## What Gets Automatically Included?

When you use `IncludeStandardHeader = true`, the appropriate header for your shader stage is included:

- **Fragment shaders**: `HeaderFrag.glsl` - Contains standard fragment shader declarations
- **Vertex/Compute/Tessellation shaders**: `HeaderVertex.glsl` - Contains standard vertex shader declarations
- **Mesh/Task shaders**: `HeaderTask.glsl` - Contains mesh shader declarations

When you use `IncludePBRFunctions = true`, the `PBRFunctions.glsl` file is included, providing:

- `PBRMaterial` struct
- `pbrShadingSimple()` function for basic PBR lighting
- `pbrShadingAdvanced()` function for advanced PBR with multiple lights
- Helper functions for IBL, BRDF calculations, etc.

## Preprocessing Features

### Include Directives

You can use `#include` directives in your shader code:

```glsl
#include "PBRFunctions.glsl"
#include "MyCustomFunctions.glsl"
```

The builder will:
1. Resolve includes from embedded resources
2. Prevent duplicate inclusions
3. Handle recursive includes
4. Report errors for missing files

### Defines

Add preprocessor defines programmatically:

```csharp
var result = GlslHeaders.BuildShader()
    .WithDefine("USE_SHADOWS")
    .WithDefine("SHADOW_MAP_SIZE", "2048")
    .Build();
```

This generates:
```glsl
#define USE_SHADOWS
#define SHADOW_MAP_SIZE 2048
```

### Version Handling

The builder automatically handles `#version` directives:
- Extracts existing version directive from your shader
- Places it at the top of the final shader
- Defaults to `#version 460` if not specified

## Advanced Usage

### Custom Include Resolution

Currently, includes are resolved from embedded resources in the assembly. Future versions may support custom include resolvers.

### Multiple Shader Stages

Compile multiple stages for a complete pipeline:

```csharp
var compiler = GlslHeaders.CreateCompiler();

var vsResult = compiler.CompileVertexShader(vertexSource);
var fsResult = compiler.CompileFragmentShaderWithPBR(fragmentSource);

if (vsResult.Success && fsResult.Success)
{
    // Create your pipeline with vsResult.Source and fsResult.Source
}
```

### Disable Caching

For debugging or development:

```csharp
var result = GlslHeaders.BuildShader()
    .WithSource(myShader)
    .WithoutCache()
    .Build();
```

## Examples

See `ShaderBuildingExamples.cs` for complete working examples:

1. **Example1_SimplePBRShader** - Basic PBR shader compilation
2. **Example2_FluentBuilder** - Using the fluent builder API
3. **Example3_MultipleStages** - Compiling vertex and fragment shaders
4. **Example4_CustomOptions** - Using custom build options
5. **Example5_LocalCache** - Working with a local cache
6. **Example6_ProcessExampleShader** - Processing a complete PBR shader

## Performance Considerations

1. **Caching**: The shader building system uses caching by default. Identical shaders with the same options will be served from cache, avoiding redundant preprocessing.

2. **Cache Settings**: The global cache stores up to 200 entries by default. You can create local caches with custom limits:
   ```csharp
   var cache = new ShaderCache(maxEntries: 50, expirationTime: TimeSpan.FromMinutes(10));
   ```

3. **LRU Eviction**: When the cache is full, least recently used entries are automatically evicted.

4. **Thread Safety**: All operations are thread-safe, allowing parallel shader compilation.

## Troubleshooting

### Common Issues

**Issue**: "Shader file not found in embedded resources"
- **Solution**: Ensure the shader file is marked as an embedded resource in the .csproj file

**Issue**: "PBRMaterial struct not found"
- **Solution**: Enable `IncludePBRFunctions = true` in your build options

**Issue**: "Duplicate definition errors"
- **Solution**: The builder prevents duplicate includes, but make sure you're not manually including and also enabling automatic inclusion

### Debug Mode

Enable debug information:

```csharp
var options = new ShaderBuildOptions
{
    EnableDebug = true
};
```

This adds comments showing which files were included and where they came from.

## Integration with Graphics Context

### Extension Methods for One-Step Compilation

The shader builder can be integrated with `IContext` for one-step build-and-compile operations:

```csharp
using HelixToolkit.Nex.Shaders;

// Build and compile in one step
var (buildResult, shaderModule) = context.BuildAndCompileFragmentShaderWithPBR(
    myShaderSource,
    debugName: "MyPBRShader"
);

if (buildResult.Success && shaderModule.Valid)
{
    // Use shader module immediately
    var pipeline = context.CreateRenderPipeline(new RenderPipelineDesc
    {
        FragementShader = shaderModule
    });
}
```

### Fluent Builder with Context

```csharp
var (buildResult, shaderModule) = context.BuildAndCompileShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(myShader)
    .WithPBRFunctions()
    .WithDefine("MAX_LIGHTS", "8")
    .WithDebugName("AdvancedShader")
    .Build();
```

### Available Extension Methods

- `BuildAndCompileShader()` - Build and compile for any shader stage
- `BuildAndCompileFragmentShaderWithPBR()` - Fragment shader with PBR
- `BuildAndCompileVertexShader()` - Vertex shader

### Benefits of Integrated Approach

1. **Single API Call** - No need for separate preprocess + compile steps
2. **Better Error Reporting** - Both preprocessing and compilation errors in one result
3. **Simplified Code** - Less boilerplate in shader creation
4. **Still Flexible** - Traditional two-step approach still available when needed

### When to Use Each Approach

**Use Extension Methods When:**
- You want immediate SPIR-V compilation
- You don't need to inspect preprocessed source
- You're creating production shaders

**Use Two-Step Approach When:**
- You need to inspect/modify preprocessed source
- You're debugging shader preprocessing
- You want to cache preprocessed source separately

## Integration with Vulkan/OpenGL

The processed shader source can be used directly with any graphics API:

```csharp
// One-step with Vulkan context
var (result, module) = context.BuildAndCompileFragmentShaderWithPBR(source);

// Two-step for more control
var result = compiler.CompileFragmentShaderWithPBR(source);

if (result.Success)
{
    // For Vulkan (via IContext)
    var module = context.CreateShaderModuleGlsl(result.Source, ShaderStage.Fragment);
    
    // Or inspect the source
    Console.WriteLine($"Preprocessed: {result.Source.Length} characters");
}
```

## Best Practices

1. **Use the Global Cache**: Unless you have specific requirements, use the default global cache for best performance.

2. **Specify Version**: Always specify `#version` in your shaders for clarity.

3. **Use Fluent API**: The builder pattern makes shader configuration more readable.

4. **Handle Errors**: Always check `result.Success` before using the compiled shader.

5. **Compile Once**: Compile shaders during initialization, not during rendering.

## Future Enhancements

Planned features for future versions:

- File system include resolution
- Custom include resolvers
- Shader optimization passes
- SPIR-V compilation integration
- Shader validation
- Hot-reload support for development

## See Also

- `PBR_README.md` - Documentation for PBR functions
- `HeaderFrag.glsl` - Standard fragment shader header
- `PBRFunctions.glsl` - PBR helper functions
- `ExamplePBRShader.frag` - Example PBR shader

## Testing

Run the unit test suite in the `HelixToolkit.Nex.Shaders.Tests` project:

```csharp
// From Visual Studio Test Explorer or command line:
dotnet test HelixToolkit.Nex.Shaders.Tests.csproj
```

The test suite includes:
1. **TestBasicCompilation** - Basic shader compilation
2. **TestPBRInclusion** - PBR functions inclusion
3. **TestDefines** - Preprocessor defines
4. **TestDefineWithValue** - Defines with values
5. **TestCaching** - Cache functionality
6. **TestCacheEviction** - LRU cache eviction
7. **TestMultipleStages** - Multiple shader stages
8. **TestFluentBuilder** - Fluent builder API
9. **TestFluentBuilderWithPBR** - Fluent builder with PBR
10. **TestCommentStripping** - Comment removal
11. **TestVersionDirectiveHandling** - Version directive processing
12. **TestDefaultVersionDirective** - Default version handling
13. **TestPBRMaterialStructure** - PBR material structure
14. **TestAllShaderStages** - All shader stage support
15. **TestEmptyShaderSource** - Empty shader handling
16. **TestCacheStatistics** - Cache statistics
17. **TestFactoryMethods** - Factory method patterns
18. **TestGetHeaderDirectly** - Direct header access

All tests use MSTest framework and are organized by test categories:
- `ShaderBuilding` - Core building functionality
- `PBR` - PBR-related tests
- `Preprocessor` - Preprocessing features
- `Caching` - Cache functionality
- `FluentAPI` - Fluent builder tests
- `ErrorHandling` - Error scenarios
- `Factory` - Factory methods
- `Headers` - Header access
