# HelixToolkit.Nex.Material

**Modern, type-safe material system with automatic shader generation for HelixToolkit.Nex**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

## Overview

HelixToolkit.Nex.Material provides a powerful material system that bridges the gap between high-level material definitions and low-level shader code. It automatically generates optimized shaders based on material properties, eliminating manual shader writing while maintaining full flexibility for custom materials.

### Key Features

✨ **Automatic Shader Generation** - Shaders are generated based on material properties  
🎯 **Type-Safe Material Definitions** - C# structs automatically sync with GLSL  
🚀 **High Performance** - Shader caching and pipeline reuse  
🔧 **Extensible** - Easy to create custom material types  
🎨 **PBR Support** - Built-in physically-based rendering  
⚡ **Forward+ Rendering** - Modern tile-based light culling  
🔗 **Bindless Resources** - GPU-driven vertex and texture access  
🏭 **Material Factory** - Auto-registration and creation by name  

## Quick Start

### Installation

Add the project reference to your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\HelixToolkit.Nex.Material\HelixToolkit.Nex.Material.csproj" />
</ItemGroup>
```

### Basic Usage

```csharp
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Graphics;

// 1. Create a PBR material
var material = new PbrMaterial();
material.Properties.Variables = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.5f,
    Roughness = 0.3f,
    Ao = 1.0f,
    Opacity = 1.0f
};

// 2. Initialize pipeline (shaders generated automatically)
var pipelineDesc = new RenderPipelineDesc
{
    Topology = Topology.Triangle,
    CullMode = CullMode.Back,
};
pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

material.InitializePipeline(context, pipelineDesc);

// 3. Use the material
cmdBuffer.BindRenderPipeline(material.Pipeline);
cmdBuffer.Draw(vertexCount);
```

### Textured Material

```csharp
var material = new PbrMaterial();

// Assign textures - shader automatically enables texture sampling
material.Properties.BaseColorTexture = albedoTexture;
material.Properties.NormalTexture = normalTexture;
material.Properties.MetallicRoughnessTexture = metallicRoughnessTexture;
material.Properties.BaseColorSampler = sampler;

material.InitializePipeline(context, pipelineDesc);
```

### Using Material Factory

```csharp
// Create materials by name
var pbrMaterial = MaterialFactory.Create("PBR") as PbrMaterial;
var unlitMaterial = MaterialFactory.Create("Unlit");

// List all registered materials
foreach (var name in MaterialFactory.GetRegisteredKeys())
{
    Console.WriteLine($"Available material: {name}");
}
```

## Architecture

### Three-Layer Design

```
┌─────────────────────────────────────┐
│     Material Layer (User-Facing)    │
│  • PbrMaterial, UnlitMaterial       │
│  • MaterialProperties (Observable)  │
│  • MaterialFactory (Registration)   │
└─────────────────────────────────────┘
               ↓
┌─────────────────────────────────────┐
│  Shader Builder Layer (Integration) │
│  • MaterialShaderBuilder            │
│  • Automatic code generation        │
│  • Define management                │
└─────────────────────────────────────┘
               ↓
┌─────────────────────────────────────┐
│  Shader Library Layer (Low-Level)   │
│  • ShaderCompiler (Preprocessing)   │
│  • GlslHeaders (PBR functions)      │
│  • Caching system                   │
└─────────────────────────────────────┘
```

## Core Components

### Material Base Classes

**`Material`** - Abstract base for all materials
- Provides `Pipeline` property for cached render pipeline
- Optional `DebugName` for debugging

**`Material<TProperties>`** - Generic material with typed properties
- Exposes strongly-typed `Properties` instance
- Properties are observable for change tracking

**`MaterialProperties`** - Base for material property bags
- Derives from `ObservableObject` for reactive updates
- Supports cloning and serialization

### Built-in Materials

#### PbrMaterial

Physically-based rendering material with full texture support:

```csharp
[MaterialName("PBR")]
public class PbrMaterial : Material<PbrMaterialProperties>
{
    public bool InitializePipeline(IContext context, RenderPipelineDesc pipelineDesc);
    public void InvalidatePipeline();
}
```

**Properties:**
- `Variables` - PBRProperties struct (albedo, metallic, roughness, etc.)
- `BaseColorTexture`, `NormalTexture`, `MetallicRoughnessTexture`, etc.
- `BaseColorSampler` - Texture sampler configuration
- Automatic texture detection and shader variant selection

### MaterialShaderBuilder

The central class for building shaders from materials:

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithDefine("USE_BASE_COLOR_TEXTURE")
    .WithCustomCode("vec3 customFunction() { ... }")
    .ForMaterial(material.Properties);

var result = builder.BuildMaterialPipeline(context, "MyMaterial");
```

**Methods:**
- `WithPBRShading(bool)` - Enable/disable PBR lighting
- `WithDefine(string, string?)` - Add preprocessor defines
- `WithCustomCode(string)` - Inject custom GLSL code
- `WithCustomMain(string)` - Replace main function
- `ForMaterial(properties)` - Auto-configure from material
- `WithBindlessVertices(bool)` - Enable bindless vertex buffers
- `WithForwardPlus(bool, config)` - Enable Forward+ rendering
- `BuildFragmentShader()` - Build fragment shader only
- `BuildMaterialPipeline(context, name)` - Build complete pipeline

### MaterialFactory

Automatic registration and creation of materials:

```csharp
// Auto-registers all materials in assembly
MaterialFactory.AutoRegisterFromAssembly(Assembly.GetExecutingAssembly());

// Create by name
var material = MaterialFactory.Create("PBR");

// List registered materials
var materials = MaterialFactory.GetRegisteredKeys();

// Custom registration
MaterialFactory.Register("Custom", () => new CustomMaterial());
```

**Features:**
- Auto-discovers materials with `[MaterialName]` attribute
- Falls back to type name (strips "Material" suffix)
- Thread-safe concurrent dictionary
- Supports both instance and property creation

## Advanced Features

### Custom Materials

Create your own material types:

```csharp
[MaterialName("Toon")]
public class ToonMaterial : Material<ToonMaterialProperties>
{
    private RenderPipelineResource _cachedPipeline = RenderPipelineResource.Null;

    public override RenderPipelineResource Pipeline => _cachedPipeline;

    public bool InitializePipeline(IContext context, RenderPipelineDesc pipelineDesc)
    {
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(false)
            .WithDefine("USE_TOON_SHADING")
            .WithCustomCode(@"
                vec3 toonShading(vec3 normal, vec3 lightDir) {
                    float intensity = dot(normal, lightDir);
                    if (intensity > 0.95) return vec3(1.0);
                    else if (intensity > 0.5) return vec3(0.6);
                    else if (intensity > 0.25) return vec3(0.4);
                    else return vec3(0.2);
                }
            ")
            .WithCustomMain(@"
                void main() {
                    vec3 normal = normalize(fragNormal);
                    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
                    vec3 color = toonShading(normal, lightDir);
                    outColor = vec4(color, 1.0);
                }
            ");

        var result = builder.BuildMaterialPipeline(context, "ToonMaterial");
        if (result.Success)
        {
            pipelineDesc.VertexShader = result.VertexShader;
            pipelineDesc.FragementShader = result.FragmentShader;
            _cachedPipeline = context.CreateRenderPipeline(pipelineDesc);
            return _cachedPipeline.Valid;
        }
        return false;
    }
}
```

### Forward+ Rendering

Modern tile-based light culling for efficient multi-light rendering:

```csharp
var config = new ForwardPlusConfig
{
    TileSize = 16,              // 16x16 pixel tiles
    MaxLightsPerTile = 256,     // Max lights per tile
    UseComputeCulling = true    // Use compute shader
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true, config);

// Handles thousands of lights efficiently
```

**Benefits:**
- Constant shading cost per pixel
- Scales to thousands of lights
- Lower memory than deferred rendering
- Works with MSAA and transparency

See [FORWARD_PLUS_GUIDE.md](FORWARD_PLUS_GUIDE.md) for details.

### Bindless Resources

GPU-driven resource access using buffer device addresses:

```csharp
var builder = new MaterialShaderBuilder()
    .WithBindlessVertices(true)
    .WithForwardPlus(true);

// No vertex buffer binding needed!
// Vertex data fetched directly via GPU address
```

**Benefits:**
- Reduces CPU overhead
- Flexible vertex formats
- Simplifies instancing
- Better GPU cache utilization

### Dynamic Updates

Handle runtime property changes efficiently:

```csharp
public void UpdateTexture(TextureResource? newTexture)
{
    bool hadTexture = material.Properties.BaseColorTexture.Valid;
    material.Properties.BaseColorTexture = newTexture ?? TextureResource.Null;
    bool hasTexture = material.Properties.BaseColorTexture.Valid;

    // Rebuild pipeline only if texture state changed
    if (hadTexture != hasTexture)
    {
        material.InvalidatePipeline();
        material.InitializePipeline(context, pipelineDesc);
    }
}
```

## Performance Best Practices

### ✅ DO

1. **Cache Pipelines** - Materials automatically cache pipelines
2. **Batch by Material** - Group draw calls by material type
3. **Invalidate Sparingly** - Only rebuild when necessary
4. **Use Texture Arrays** - Better than individual texture bindings
5. **Enable Shader Caching** - Uses global cache by default

### ❌ DON'T

1. **Don't Recreate Materials** - Reuse material instances
2. **Don't Rebuild Every Frame** - Pipelines are expensive
3. **Don't Skip Error Checking** - Check `InitializePipeline` results
4. **Don't Mix Approaches** - Use either manual or automatic shaders
5. **Don't Ignore Invalidation** - Call when textures change state

## Common Patterns

### Material Library

```csharp
public static class MaterialLibrary
{
    public static PbrMaterial CreateMetal()
    {
        var material = new PbrMaterial();
        material.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.8f, 0.8f, 0.8f),
            Metallic = 1.0f,
            Roughness = 0.2f
        };
        return material;
    }

    public static PbrMaterial CreatePlastic()
    {
        var material = new PbrMaterial();
        material.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.2f, 0.8f, 0.2f),
            Metallic = 0.0f,
            Roughness = 0.5f
        };
        return material;
    }
}
```

### Quality Levels

```csharp
public MaterialShaderResult BuildForQuality(MaterialQuality quality)
{
    var builder = new MaterialShaderBuilder().WithPBRShading(true);

    switch (quality)
    {
        case MaterialQuality.High:
            builder.WithDefine("USE_BASE_COLOR_TEXTURE");
            builder.WithDefine("USE_NORMAL_TEXTURE");
            builder.WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE");
            break;
        case MaterialQuality.Medium:
            builder.WithDefine("USE_BASE_COLOR_TEXTURE");
            break;
        case MaterialQuality.Low:
            // No textures
            break;
    }

    return builder.BuildMaterialPipeline(context, $"Material_{quality}");
}
```

## Documentation

- **[MATERIAL_SHADER_INTEGRATION.md](MATERIAL_SHADER_INTEGRATION.md)** - Complete integration guide
- **[FORWARD_PLUS_GUIDE.md](FORWARD_PLUS_GUIDE.md)** - Forward+ rendering details
- **MaterialShaderIntegrationExamples.cs** - Working code examples
- **ForwardPlusExample.cs** - Forward+ usage examples

## Dependencies

- **HelixToolkit.Nex.Graphics** - Core graphics abstractions
- **HelixToolkit.Nex.Maths** - Math types (Vector3, Matrix4x4, etc.)
- **HelixToolkit.Nex** - ObservableObject for property tracking
- **HelixToolkit.Nex.Shaders** - Shader compilation and GLSL headers
- **HelixToolkit.Nex.CodeGen** - Source generators (analyzer only)

## Examples

### Basic PBR

```csharp
var material = new PbrMaterial();
material.Properties.Variables = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.5f,
    Roughness = 0.3f
};
material.InitializePipeline(context, pipelineDesc);
```

### Textured PBR

```csharp
var material = new PbrMaterial();
material.Properties.BaseColorTexture = albedoTexture;
material.Properties.NormalTexture = normalTexture;
material.Properties.BaseColorSampler = sampler;
material.InitializePipeline(context, pipelineDesc);
```

### Custom Shader

```csharp
var builder = new MaterialShaderBuilder()
    .WithCustomMain(@"
        void main() {
            vec3 color = vec3(1.0, 0.0, 0.0);
            outColor = vec4(color, 1.0);
        }
    ");
var result = builder.BuildMaterialPipeline(context, "CustomMaterial");
```

## Troubleshooting

### Pipeline is Null/Invalid

Check if `InitializePipeline` returned true:

```csharp
if (!material.InitializePipeline(context, pipelineDesc))
{
    Console.WriteLine("Pipeline creation failed");
    // Check shader build errors
}
```

### Textures Not Working

Ensure textures AND sampler are assigned:

```csharp
material.Properties.BaseColorTexture = texture;
material.Properties.BaseColorSampler = sampler;  // Required!
material.InvalidatePipeline();
material.InitializePipeline(context, pipelineDesc);
```

### Custom Shader Errors

Verify GLSL syntax:

```csharp
var result = builder.BuildFragmentShader();
if (!result.Success)
{
    foreach (var error in result.Errors)
        Console.WriteLine(error);
}
```

## Contributing

Contributions are welcome! When adding new features:

1. Follow existing naming conventions
2. Add examples to `MaterialShaderIntegrationExamples.cs`
3. Register materials with `MaterialFactory`
4. Document shader injection points
5. Test with both textured and non-textured variants

## License

MIT License - Same as HelixToolkit.Nex project

---

**Part of [HelixToolkit.Nex](https://github.com/helix-toolkit/helix-toolkit-nex)** - Modern 3D graphics toolkit for .NET
