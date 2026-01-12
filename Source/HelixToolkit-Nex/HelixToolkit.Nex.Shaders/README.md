# HelixToolkit.Nex.Shaders - Complete Reference

**A powerful shader building system with automatic header inclusion, PBR support, and auto-generated GLSL-to-C# struct mapping for HelixToolkit.Nex.**

---

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Features](#features)
- [Auto-Generated GLSL Structs](#auto-generated-glsl-structs)
- [Shader Building System](#shader-building-system)
- [PBR Functions](#pbr-functions)
- [API Reference](#api-reference)
- [Integration Guide](#integration-guide)
- [Performance & Caching](#performance--caching)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Examples](#examples)

---

## Overview

HelixToolkit.Nex.Shaders provides three major features:

1. **Shader Building System** - Automatically includes headers, handles preprocessor directives, and provides fluent API for shader compilation
2. **PBR Lighting Functions** - Complete physically-based rendering implementation with Cook-Torrance BRDF
3. **GLSL Struct Generator** - Source generator that automatically creates C# equivalents of GLSL structs with proper memory layout

---

## Quick Start

### 1. Basic Shader Compilation with PBR

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

// Your custom fragment shader
string myShader = @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

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
    
    vec3 viewDir = vec3(0.0, 0.0, 1.0);
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    
    vec3 color = pbrShadingSimple(material, lightDir, vec3(1.0), 3.0, viewDir, vec3(0.03));
    
    outColor = vec4(color, 1.0);
}";

// Compile with automatic header and PBR inclusion
var compiler = new ShaderCompiler();
var result = compiler.CompileFragmentShaderWithPBR(myShader);

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
    .WithStandardHeader()
    .WithPBRFunctions()
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
var material = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.9f,
    Roughness = 0.2f,
    Ao = 1.0f,
    Normal = new Vector3(0, 1, 0),
    Emissive = Vector3.Zero,
    Opacity = 1.0f
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

- ? **Automatic Header Inclusion** - Correct headers based on shader stage
- ? **PBR Support** - Easy PBR functions inclusion
- ? **Preprocessor** - Handles #include and custom defines
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

### PBR Functions

- ? **Cook-Torrance BRDF** - Industry-standard PBR model
- ? **GGX Distribution** - High-quality specular reflections
- ? **Multiple Light Types** - Directional, point, and spot lights
- ? **Energy Conservation** - Physically accurate light behavior
- ? **Metallic Workflow** - Proper metallic/non-metallic rendering

---

## Auto-Generated GLSL Structs

### What Gets Generated

When you build the project, the source generator scans all `.glsl` files and extracts `struct` definitions, generating C# equivalents.

**From `PBRFunctions.glsl`:**
```glsl
struct PBRMaterial {
    vec3 albedo;
    float metallic;
    float roughness;
};
```

**Generates:**
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct PBRMaterial
{
    public System.Numerics.Vector3 Albedo;
    public float Metallic;
    public float Roughness;
}
```

### Currently Generated Structs

From `Headers/PBRFunctions.glsl`:
- **`PBRMaterial`** (7 fields) - Material properties for physically-based rendering
- **`Light`** (8 fields) - Light source definition (directional, point, spot)

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

### Usage Examples

See `GlslStructUsageExamples.cs` for complete examples:

```csharp
// Creating materials
var metallicMaterial = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.9f,
    Roughness = 0.2f,
    Ao = 1.0f,
    Normal = new Vector3(0, 1, 0),
    Emissive = Vector3.Zero,
    Opacity = 1.0f
};

// Creating lights
var directionalLight = new Light
{
    Type = 0,
    Direction = Vector3.Normalize(new Vector3(-1, -1, -0.5f)),
    Color = new Vector3(1.0f, 0.95f, 0.8f),
    Intensity = 1.5f,
    Range = 0.0f
};

var pointLight = new Light
{
    Type = 1,
    Position = new Vector3(5, 3, 2),
    Color = new Vector3(1.0f, 1.0f, 1.0f),
    Intensity = 2.0f,
    Range = 10.0f
};
```

### Adding New Structs

1. Define your struct in any `.glsl` file in the `Headers` folder
2. Rebuild the project
3. The struct will be automatically available in C#

### Viewing Generated Code

Generated files are located in:
```
obj/GeneratedFiles/HelixToolkit.Nex.CodeGen/HelixToolkit.Nex.CodeGen.GlslStructGenerator/
```

To output to a visible location during build:
```bash
dotnet build /p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=obj/GeneratedFiles
```

---

## Shader Building System

### What Gets Automatically Included

When you use `IncludeStandardHeader = true`, the appropriate header for your shader stage is included:

- **Fragment shaders**: `HeaderFrag.glsl`
- **Vertex/Compute/Tessellation**: `HeaderVertex.glsl`
- **Mesh/Task**: `HeaderTask.glsl`

When you use `IncludePBRFunctions = true`, `PBRFunctions.glsl` is included with:
- PBR material and light structures
- Complete BRDF functions
- Helper functions for lighting calculations

### Stage-Specific Methods

```csharp
var compiler = new ShaderCompiler();

compiler.CompileVertexShader(source);
compiler.CompileFragmentShaderWithPBR(source);
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
    IncludeStandardHeader = true,
    IncludePBRFunctions = true,
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
#include "PBRFunctions.glsl"
#include "MyCustomFunctions.glsl"
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

### ShaderBuildResult

```csharp
public class ShaderBuildResult
{
    public bool Success { get; set; }              // Compilation successful?
    public string? Source { get; set; }            // Final processed source
    public List<string> Errors { get; set; }       // Errors
    public List<string> Warnings { get; set; }     // Warnings
    public List<string> IncludedFiles { get; set; } // Included files
}
```

---

## PBR Functions

### Overview

Complete physically-based rendering implementation using:
- **Cook-Torrance BRDF** model
- **GGX/Trowbridge-Reitz** normal distribution
- **Schlick-GGX** geometry function (Smith's method)
- **Schlick's approximation** for Fresnel reflectance
- **Lambertian diffuse** for energy conservation

### PBRMaterial Structure

```glsl
struct PBRMaterial {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    vec3 normal;           // World-space normal
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity/alpha [0..1]
};
```

### Light Structure

```glsl
struct Light {
    vec3 position;         // Light position (world space)
    vec3 direction;        // Light direction
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
    int type;              // 0=directional, 1=point, 2=spot
    float range;           // Light range
    float innerConeAngle;  // Inner cone (spot lights)
    float outerConeAngle;  // Outer cone (spot lights)
};
```

### Main Shading Functions

```glsl
// Full PBR shading with multiple lights
vec3 pbrShading(PBRMaterial material, vec3 fragPos, vec3 viewDir, 
                Light lights[16], int numLights, vec3 ambientColor);

// Simplified PBR with single directional light
vec3 pbrShadingSimple(PBRMaterial material, vec3 lightDir, vec3 lightColor, 
                      float lightIntensity, vec3 viewDir, vec3 ambientColor);
```

### BRDF Component Functions

```glsl
// Fresnel reflectance (Schlick's approximation)
vec3 fresnelSchlick(float cosTheta, vec3 F0);

// GGX Normal Distribution Function
float distributionGGX(vec3 N, vec3 H, float roughness);

// Smith's Geometry function
float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness);

// Cook-Torrance specular BRDF
vec3 cookTorranceBRDF(vec3 N, vec3 V, vec3 L, vec3 H, vec3 F0, float roughness);

// Lambertian diffuse BRDF
vec3 lambertianDiffuse(vec3 albedo);
```

### Usage Example

```glsl
void main() {
    // Setup material
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.1, 0.1);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.normal = normalize(fragNormal);
    material.emissive = vec3(0.0);
    material.opacity = 1.0;
    
    // Setup light
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 lightColor = vec3(1.0);
    float lightIntensity = 3.0;
    vec3 viewDir = normalize(cameraPosition - fragPosition);
    
    // Render with PBR
    vec3 color = pbrShadingSimple(
        material, lightDir, lightColor, lightIntensity,
        viewDir, vec3(0.03)
    );
    
    // Tone mapping + gamma correction
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0/2.2));
    
    outColor = vec4(color, 1.0);
}
```

### Multiple Lights Example

```glsl
void main() {
    // ... setup material ...
    
    Light lights[3];
    
    // Directional light
    lights[0].type = 0;
    lights[0].direction = normalize(vec3(-0.5, -1.0, -0.3));
    lights[0].color = vec3(1.0, 0.95, 0.9);
    lights[0].intensity = 3.0;
    
    // Point light
    lights[1].type = 1;
    lights[1].position = vec3(5.0, 3.0, 2.0);
    lights[1].color = vec3(1.0, 0.5, 0.2);
    lights[1].intensity = 10.0;
    lights[1].range = 15.0;
    
    // Spot light
    lights[2].type = 2;
    lights[2].position = vec3(-2.0, 5.0, 0.0);
    lights[2].direction = normalize(vec3(0.5, -1.0, 0.0));
    lights[2].color = vec3(0.2, 0.5, 1.0);
    lights[2].intensity = 20.0;
    lights[2].range = 20.0;
    lights[2].innerConeAngle = cos(radians(15.0));
    lights[2].outerConeAngle = cos(radians(25.0));
    
    vec3 viewDir = normalize(cameraPosition - fragPosition);
    vec3 color = pbrShading(material, fragPosition, viewDir, lights, 3, vec3(0.03));
    
    outColor = vec4(color, material.opacity);
}
```

### Metallic Workflow

- **Dielectric materials** (metallic = 0): F0 = 0.04, colored diffuse
- **Metallic materials** (metallic = 1): F0 = albedo, no diffuse
- **In-between values**: Linear interpolation

---

## API Reference

### ShaderCompiler

```csharp
// Create compiler
var compiler = new ShaderCompiler(useGlobalCache: true);

// Compile for different stages
compiler.CompileFragmentShaderWithPBR(source);
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
    public bool IncludeStandardHeader { get; set; }
    public bool IncludePBRFunctions { get; set; }
    public bool StripComments { get; set; }
    public bool EnableDebug { get; set; }
    public Dictionary<string, string> Defines { get; set; }
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
        var result = _compiler.CompileFragmentShaderWithPBR(fragmentSource);
        
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
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
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
        var result = compiler.CompileFragmentShaderWithPBR(shaderCode);
        
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
- **Solution**: Ensure the file is marked as `EmbeddedResource` in .csproj

**Issue**: "PBRMaterial struct not found"
- **Solution**: Enable `IncludePBRFunctions = true`

**Issue**: "Duplicate definition errors"
- **Solution**: The builder prevents duplicates, but don't manually include files that are auto-included

**Issue**: "Generated C# structs not found"
- **Solution**: Rebuild the project to trigger source generator

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
var result = compiler.CompileFragmentShaderWithPBR(source);

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
var result = compiler.CompileFragmentShaderWithPBR(@"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial mat;
    mat.albedo = vec3(0.8, 0.2, 0.1);
    mat.metallic = 0.5;
    mat.roughness = 0.3;
    mat.ao = 1.0;
    mat.normal = normalize(fragNormal);
    mat.emissive = vec3(0.0);
    mat.opacity = 1.0;
    
    vec3 color = pbrShadingSimple(mat, vec3(0, -1, 0), vec3(1), 3.0, vec3(0, 0, 1), vec3(0.03));
    outColor = vec4(color, 1.0);
}");

if (result.Success)
{
    UseShader(result.Source);
}
```

### Example 2: Fluent Builder

```csharp
var result = GlslHeaders.BuildShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(myShader)
    .WithStandardHeader()
    .WithPBRFunctions()
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
void main() {
    PBRMaterial mat;
    " + (albedo != null ? "mat.albedo = texture(albedoTex, uv).rgb;" : "mat.albedo = vec3(0.8);") + @"
    mat.metallic = 0.0;
    mat.roughness = 0.5;
    mat.normal = " + (normal != null ? "getNormal(normalTex, uv);" : "normalize(fragNormal);") + @"
    // ... render ...
}";

        return new ShaderCompiler().CompileFragmentShaderWithPBR(shader).Source!;
    }
}
```

### Example 4: Using Generated C# Structs

```csharp
// Create materials
var glass = new PBRMaterial
{
    Albedo = new Vector3(0.95f, 0.95f, 0.98f),
    Metallic = 0.0f,
    Roughness = 0.05f,
    Ao = 1.0f,
    Normal = new Vector3(0, 1, 0),
    Emissive = Vector3.Zero,
    Opacity = 0.3f
};

var metal = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.8f, 0.8f),
    Metallic = 1.0f,
    Roughness = 0.2f,
    Ao = 1.0f,
    Normal = new Vector3(0, 1, 0),
    Emissive = Vector3.Zero,
    Opacity = 1.0f
};

// Upload to GPU
context.Upload(materialBuffer, 0, ref glass);
context.Upload(materialBuffer, sizeof(PBRMaterial), ref metal);
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
