# HelixToolkit.Nex.Shaders - Complete Reference

**A powerful shader building system with include processing, PBR support, GPU Culling utilities, and auto-generated GLSL-to-C# struct mapping for HelixToolkit.Nex.**

---

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Features](#features)
- [Auto-Generated GLSL Structs](#auto-generated-glsl-structs)
- [Shader Building System](#shader-building-system)
- [PBR Functions](#pbr-functions)
- [GPU Culling Utilities](#gpu-culling-utilities)
- [API Reference](#api-reference)
- [Integration Guide](#integration-guide)
- [Performance & Caching](#performance--caching)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Examples](#examples)

---

## Overview

HelixToolkit.Nex.Shaders provides four major features:

1. **Shader Building System** - Handles `#include` directives (including from embedded resources), preprocessor defines, and provides a fluent API for shader compilation
2. **PBR Lighting Functions** - Complete physically-based rendering implementation with Cook-Torrance BRDF
3. **GPU Culling Utilities** - Ready-to-use compute shader generation for Forward+ Light Culling and Frustum Culling
4. **GLSL Struct Generator** - Source generator that automatically creates C# equivalents of GLSL structs with proper memory layout

---

## Quick Start

### 1. Basic Shader Compilation with PBR

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

// Your custom fragment shader
// Note: Use #include to pull in standard headers and PBR functions
string myShader = @"
#include ""HxHeaders/HeaderFrag.glsl""
#include ""HxHeaders/PBRFunctions.glsl""

layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    // Use PBR functions directly - they're included via the header!
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    // material.normal requires calculation or input
    
    vec3 viewDir = vec3(0.0, 0.0, 1.0);
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    
    // Example usage of a PBR function (check PBRFunctions.glsl for signature)
    // vec3 color = pbrShadingSimple(material, ...); 
    
    outColor = vec4(1.0, 0.0, 0.0, 1.0);
}";

// Compile the shader
var compiler = new ShaderCompiler();
var result = compiler.CompileFragmentShader(myShader);

if (result.Success)
{
    string finalShader = result.Source;
    // Use with your graphics API...
}
```

### 2. Using Fluent Builder API

```csharp
var result = GlslHeaders.BuildShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(myShader)
    .WithDefine("USE_ADVANCED_LIGHTING")
    .WithDefine("MAX_LIGHTS", "8")
    .StripComments()
    .Build();
```

### 3. Using Auto-Generated C# Structs

```csharp
using HelixToolkit.Nex.Shaders;
using System.Numerics;

// Structs are automatically generated from PBRFunctions.glsl
var material = new PBRProperties
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.9f,
    Roughness = 0.2f,
    // ...
};

var light = new Light
{
    Type = 0,  // Directional
    Direction = new Vector3(-1, -1, -0.5f),
    Color = new Vector3(1.0f, 0.95f, 0.8f),
    Intensity = 1.5f
};

// Upload to GPU buffer (structs have proper memory layout)
// buffer.Upload(material);
```

---

## Features

### Shader Building System

- ? **Include System** - processing of `#include` directives from embedded resources
- ? **Preprocessor** - Handles custom defines
- ? **Caching** - Built-in LRU cache for performance
- ? **Fluent API** - Clean builder pattern
- ? **Thread-Safe** - All operations are thread-safe
- ? **Comment Stripping** - Optional comment removal

### Auto-Generated Structs

- ? **Type Safety** - C# structs match GLSL definitions exactly
- ? **Memory Layout** - `StructLayout` ensures GPU compatibility
- ? **Auto-Sync** - Changes to GLSL automatically update C# code
- ? **IntelliSense** - Full IDE support with documentation
- ? **Source Generator** - Zero-runtime cost code generation

### GPU Culling

- ? **Forward+ Light Culling** - Compute shader generation for tiled light culling
- ? **Frustum Culling** - GPU-based frustum culling generation
- ? **Instancing Support** - Culling support for instanced rendering

### PBR Functions

- ? **Cook-Torrance BRDF** - Industry-standard PBR model
- ? **GGX Distribution** - High-quality specular reflections
- ? **Metallic Workflow** - Proper metallic/non-metallic rendering

---

## Auto-Generated GLSL Structs

### What Gets Generated

When you build the project, the source generator scans all `.glsl` files and extracts `struct` definitions marked with `@code_gen`, generating C# equivalents.

**From `PBRFunctions.glsl` (example):**
```glsl
@code_gen
struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
};
```

**Generates:**
```csharp
[StructLayout(LayoutKind.Sequential, Pack=16)]
public struct PBRProperties
{
    public System.Numerics.Vector3 Albedo;
    public float Metallic;
    public float Roughness;
    public static readonly unsafe uint SizeInBytes = (uint)sizeof(PBRProperties);
}
```

### Currently Generated Structs

From `HxHeaders/LightStruct.glsl`:
- **`Light`** - Light source definition (directional, point, spot)
- **`DirectionalLights`** - Container for directional lights

From `Frag/psPBRTemplate.glsl`:
- **`PBRProperties`** - Material properties for physically-based rendering

### Type Mapping

| GLSL Type | C# Type |
|-----------|---------|
| `float` | `float` |
| `int` | `int` |
| `uint` | `uint` |
| `bool` | `bool` |
| `vec2` | `System.Numerics.Vector2` |
| `vec3` | `System.Numerics.Vector3` |
| `vec4` | `System.Numerics.Vector4` |
| `mat4` | `System.Numerics.Matrix4x4` |
| User-defined | Same name in C# |

---

## Shader Building System

### Include Resolution

The shader builder automatically resolves `#include` directives. By default, it looks for files in the embedded resources of the library.

Use the `HxHeaders/` prefix to include standard library headers:

```glsl
// Include standard fragment shader header
#include "HxHeaders/HeaderFrag.glsl"

// Include PBR functions
#include "HxHeaders/PBRFunctions.glsl"
```
Common headers available:
- `HxHeaders/HeaderFrag.glsl` - Standard Fragment header
- `HxHeaders/HeaderVertex.glsl` - Standard Vertex/Tessellation header
- `HxHeaders/HeaderCompute.glsl` - Standard Compute header
- `HxHeaders/HeaderTask.glsl` - Standard Mesh/Task header
- `HxHeaders/PBRFunctions.glsl` - PBR lighting functions
- `HxHeaders/LightStruct.glsl` - Light structure definitions

### Stage-Specific Methods

```csharp
var compiler = new ShaderCompiler();

compiler.CompileVertexShader(source);
compiler.CompileFragmentShader(source);
compiler.CompileComputeShader(source);
compiler.CompileGeometryShader(source);
compiler.CompileTessControlShader(source);
compiler.CompileTessEvalShader(source);
compiler.CompileMeshShader(source);
compiler.CompileTaskShader(source);
```

### Custom Build Options

```csharp
var options = new ShaderBuildOptions
{
    StripComments = false,
    EnableDebug = false,
    Defines = new Dictionary<string, string>
    {
        { "USE_LIGHTING", "1" },
        { "MAX_LIGHTS", "4" }
    }
};

var result = compiler.Compile(ShaderStage.Fragment, source, options);
```

### Preprocessor Features

#### Include Directives

```glsl
#include "HxHeaders/PBRFunctions.glsl"
```

The builder automatically:
- Resolves includes from embedded resources
- Prevents duplicate inclusions
- Handles recursive includes
- Reports errors for missing files

#### Defines

```csharp
var result = GlslHeaders.BuildShader()
    .WithDefine("USE_SHADOWS")
    .WithDefine("SHADOW_MAP_SIZE", "2048")
    .Build();
```

Generates:
```glsl
#define USE_SHADOWS
#define SHADOW_MAP_SIZE 2048
```

---

## PBR Functions

### Overview

Complete physically-based rendering implementation using:
- **Cook-Torrance BRDF** model
- **GGX/Trowbridge-Reitz** normal distribution
- **Schlick-GGX** geometry function (Smith's method)
- **Schlick's approximation** for Fresnel reflectance

### PBRProperties Structure

```glsl
struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    vec3 emissive;         // Emissive color
    float roughness;       // Roughness factor [0..1]
    vec3 ambient;          // Ambient color
    float ao;              // Ambient occlusion [0..1]
    float opacity;         // Opacity/alpha [0..1]
    float vertexColorMix;  // Vertex color mix factor
    uint albedoTexIndex;
    uint normalTexIndex;
    uint metallicRoughnessTexIndex;
    uint samplerIndex;
    vec2 _padding;
};
```

To use these functions, simply include the header:
```glsl
#include "HxHeaders/PBRFunctions.glsl"
```

---

## GPU Culling Utilities

HelixToolkit.Nex.Shaders provides built-in generation of compute shaders for advanced culling techniques.

### Forward+ Light Culling

Generates a compute shader for screen-space tile-based light culling.

```csharp
using HelixToolkit.Nex.Shaders;

var config = new ForwardPlusLightCulling.Config
{
    TileSize = 16,
    MaxLightsPerTile = 32
};

// Returns the full GLSL compute shader source
string source = ForwardPlusLightCulling.GenerateComputeShader(config);
```

### GPU Frustum Culling

Generates compute shaders for indirect draw frustum culling.

```csharp
using HelixToolkit.Nex.Shaders;

// Generate simpler culling shader (MultiMeshSingleInstance)
string cullShader = GpuFrustumCulling.GenerateComputeShader(GpuFrustumCulling.CullMode.MultiMeshSingleInstance);

// Generate instancing culling shader (SingleMeshInstancing)
string instancingShader = GpuFrustumCulling.GenerateComputeShader(GpuFrustumCulling.CullMode.SingleMeshInstancing);
```

### Culling Helpers

Use `Helpers` to generate culling constants for the shaders.

```csharp
var cullConstants = Helpers.CreateCullConstants(viewMatrix, projectionMatrix);
// Update existing
Helpers.UpdateCullConstants(ref cullConstants, view, proj);
```

---

## API Reference

### ShaderCompiler

```csharp
// Create compiler
var compiler = new ShaderCompiler(useGlobalCache: true);

// Compile for different stages
compiler.CompileFragmentShader(source);
compiler.CompileVertexShader(source);
compiler.CompileComputeShader(source);

// Generic compile
var result = compiler.Compile(ShaderStage.Fragment, source, options);

// Cache management
compiler.ClearCache();
var stats = compiler.GetCacheStatistics();
```

### ShaderBuildOptions

```csharp
public class ShaderBuildOptions
{
    public bool StripComments { get; set; }
    public bool EnableDebug { get; set; }
    public Dictionary<string, string> Defines { get; set; }
    public Func<string, string?>? IncludeProvider { get; set; }
}
```

### ShaderCache

```csharp
// Global cache (default)
var compiler = new ShaderCompiler(useGlobalCache: true);

// Local cache
var cache = new ShaderCache(
    maxEntries: 100,
    expirationTime: TimeSpan.FromMinutes(30)
);
var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

// Statistics
var stats = compiler.GetCacheStatistics();
Console.WriteLine($"Entries: {stats.TotalEntries}");
Console.WriteLine($"Avg accesses: {stats.AverageAccessCount}");
```

### Factory Methods

```csharp
// Create compiler
var compiler = GlslHeaders.CreateCompiler();

// Create builder
var builder = GlslHeaders.BuildShader();

// Get headers directly
string fragmentHeader = GlslHeaders.GetShaderHeader(ShaderStage.Fragment);
string pbrFunctions = GlslHeaders.GetGlslShaderPBRFunction();
```

---

## Integration Guide

### Basic Integration

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

public class MyRenderer
{
    private readonly ShaderCompiler _compiler;

    public MyRenderer()
    {
        _compiler = GlslHeaders.CreateCompiler();
    }

    public void InitializeShaders()
    {
        string fragmentSource = LoadShaderFromFile("shader.frag");
        var result = _compiler.CompileFragmentShader(fragmentSource);
        
        if (!result.Success)
        {
            throw new Exception($"Shader compilation failed: {string.Join(", ", result.Errors)}");
        }
        
        CreateGraphicsShader(result.Source);
    }
}
```

### Dynamic Shader Variants

```csharp
public class ShaderVariantManager
{
    private readonly ShaderCompiler _compiler = new();

    public string GetVariant(string baseShader, bool shadows, int maxLights)
    {
        var options = new ShaderBuildOptions
        {
            Defines = new Dictionary<string, string>
            {
                { "USE_SHADOWS", shadows ? "1" : "0" },
                { "MAX_LIGHTS", maxLights.ToString() }
            }
        };

        var result = _compiler.Compile(ShaderStage.Fragment, baseShader, options);
        return result.Source!;
    }
}
```

### Material System Integration

```csharp
public class PBRMaterialGenerator
{
    public string GenerateShader(Material material)
    {
        var shaderCode = BuildShaderFromMaterial(material);
        
        var compiler = GlslHeaders.CreateCompiler();
        var result = compiler.CompileFragmentShader(shaderCode);
        
        return result.Success ? result.Source! : throw new Exception("Compilation failed");
    }
}
```

---

## Performance & Caching

### Caching Benefits

1. **Speed**: Identical shaders are served from cache
2. **Memory**: LRU eviction keeps memory usage bounded
3. **Thread-Safe**: Concurrent access supported

### Cache Configuration

```csharp
// Global cache (default, 200 entries)
var compiler = new ShaderCompiler(useGlobalCache: true);

// Custom local cache
var cache = new ShaderCache(
    maxEntries: 50,
    expirationTime: TimeSpan.FromMinutes(10)
);
var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

// Disable caching
var builder = GlslHeaders.BuildShader().WithoutCache();
```

### Performance Tips

? Compile shaders at startup, not runtime
? Use the global cache for best performance
? Compile on background threads for long operations
? Clear cache during development for fresh builds
? Monitor cache statistics to optimize settings

---

## Testing

### Running Tests

```bash
# All tests
dotnet test HelixToolkit.Nex.Shaders.Tests.csproj

# Specific category
dotnet test --filter "TestCategory=PBR"
dotnet test --filter "TestCategory=Caching"

# From Visual Studio: Test Explorer ? Run All
```

### Test Categories

- `ShaderBuilding` - Core building functionality
- `PBR` - PBR-related tests
- `Preprocessor` - Preprocessing features
- `Caching` - Cache functionality
- `FluentAPI` - Fluent builder tests
- `ErrorHandling` - Error scenarios
- `Factory` - Factory methods
- `Headers` - Header access

### Test Coverage

18 comprehensive tests covering:
- Basic compilation
- PBR inclusion
- Preprocessor defines
- Caching and eviction
- Multiple shader stages
- Fluent builder API
- Comment stripping
- Version directives
- Error handling

---

## Troubleshooting

### Common Issues

**Issue**: "Shader file not found in embedded resources"
- **Solution**: Ensure the file is marked as `EmbeddedResource` in .csproj and check the path (use `HxHeaders/...`).

**Issue**: "PBRProperties struct not found"
- **Solution**: Ensure you added `#include "HxHeaders/PBRFunctions.glsl"` to your shader.

**Issue**: "Duplicate definition errors"
- **Solution**: The builder prevents duplicates for includes, but check your defines.

**Issue**: "Generated C# structs not found"
- **Solution**: Rebuild the project to trigger source generator and ensure structs in glsl are marked `with @code_gen`.

### Debug Mode

```csharp
var options = new ShaderBuildOptions { EnableDebug = true };

// Print details
Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Source length: {result.Source?.Length}");
Console.WriteLine($"Errors: {result.Errors.Count}");
Console.WriteLine($"Included files: {string.Join(", ", result.IncludedFiles)}");
```

### Error Handling

```csharp
var result = compiler.CompileFragmentShader(source);

if (!result.Success)
{
    Console.WriteLine("Compilation failed!");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}

foreach (var warning in result.Warnings)
{
    Console.WriteLine($"Warning: {warning}");
}
```

---

## Examples

### Example 1: Simple PBR Shader

```csharp
var compiler = new ShaderCompiler();

var shaderSource = @"
#include ""HxHeaders/PBRFunctions.glsl""
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRProperties mat;
    mat.albedo = vec3(0.8, 0.2, 0.1);
    mat.metallic = 0.5;
    mat.roughness = 0.3;
    mat.ao = 1.0;
    mat.normal = normalize(fragNormal);
    mat.emissive = vec3(0.0);
    mat.opacity = 1.0;
    
    // ... use PBR functions ...
    outColor = vec4(mat.albedo, 1.0);
}";

var result = compiler.CompileFragmentShader(shaderSource);

if (result.Success)
{
    UseShader(result.Source);
}
```

### Example 2: Fluent Builder

```csharp
var result = GlslHeaders.BuildShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(myShader) // Ensure myShader contains necessary #includes
    .WithDefine("USE_SHADOWS")
    .WithDefine("MAX_LIGHTS", "8")
    .StripComments()
    .Build();
```

### Example 3: Material Generator

```csharp
public class MaterialShaderBuilder
{
    public string BuildPBRShader(Texture2D? albedo, Texture2D? normal)
    {
        var shader = @"
#include ""HxHeaders/PBRFunctions.glsl""
void main() {
    PBRProperties mat;
    " + (albedo != null ? "mat.albedo = texture(albedoTex, uv).rgb;" : "mat.albedo = vec3(0.8);") + @"
    mat.metallic = 0.0;
    mat.roughness = 0.5;
    mat.normal = " + (normal != null ? "getNormal(normalTex, uv);" : "normalize(fragNormal);") + @"
    // ... render ...
}";

        return new ShaderCompiler().CompileFragmentShader(shader).Source!;
    }
}
```

### Example 4: Using Generated C# Structs

```csharp
// Create materials
var glass = new PBRProperties
{
    Albedo = new Vector3(0.95f, 0.95f, 0.98f),
    Metallic = 0.0f,
    Roughness = 0.05f,
    // ...
};

var metal = new PBRProperties
{
    Albedo = new Vector3(0.8f, 0.8f, 0.8f),
    Metallic = 1.0f,
    Roughness = 0.2f,
    // ...
};

// Upload to GPU
context.Upload(materialBuffer, 0, ref glass);
context.Upload(materialBuffer, sizeof(PBRProperties), ref metal);
```

See also:
- `ShaderBuildingExamples.cs` - Complete working examples
- `GlslStructUsageExamples.cs` - Struct usage patterns
- `IntegratedShaderBuildingExamples.cs` - Advanced integration

---

## Best Practices

1. **Compile Once**: Compile shaders during initialization
2. **Use Global Cache**: Default cache provides best performance
3. **Handle Errors**: Always check `result.Success`
4. **Fluent API**: Use builder pattern for readability
5. **Separate Logic**: Keep shader sources in separate files
6. **Version Shaders**: Include version info for debugging
7. **Test Variants**: Test all shader variants during CI/CD
8. **Profile**: Use cache statistics to optimize

---

## Future Enhancements

### Planned Features

- File system include resolution
- Custom include resolvers
- Shader optimization passes
- SPIR-V compilation integration
- Shader validation
- Hot-reload support
- IBL (Image-Based Lighting) support
- Subsurface scattering
- Anisotropic reflections

---

## References

### PBR Resources
- [Real Shading in Unreal Engine 4](https://blog.selfshadow.com/publications/s2013-shading-course/karis/s2013_pbs_epic_notes_v2.pdf) - Epic Games
- [Physically Based Shading at Disney](https://media.disneyanimation.com/uploads/production/publication_asset/48/asset/s2012_pbs_disney_brdf_notes_v3.pdf) - Disney
- [LearnOpenGL PBR Theory](https://learnopengl.com/PBR/Theory) - Joey de Vries

### Source Generator Documentation
- See `HelixToolkit.Nex.CodeGen/GLSL_STRUCT_GENERATOR_README.md` for detailed generator documentation

---

## License

MIT License - Same as HelixToolkit.Nex project

## Contributing

Contributions are welcome! Please follow the existing code style and add tests for new features.

---

**Version**: 1.0.0  
**Created for**: HelixToolkit.Nex  
**Last Updated**: 2024
