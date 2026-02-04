# Material System & Shader Library Integration Guide

## Overview

This guide explains how to integrate the **Material System** (`HelixToolkit.Nex.Material`) with the **Shader Library** (`HelixToolkit.Nex.Shaders`) to create custom materials easily.

The integration provides:
- ? **Automatic shader generation** based on material properties
- ? **Type-safe material definitions** with C# structs matching GLSL
- ? **Easy customization** through fluent builder API
- ? **Performance** through shader caching and pipeline reuse
- ? **Extensibility** for custom material types

---

## Architecture

### Three-Layer Design

```
???????????????????????????????????????????????
?     Material Layer (User-Facing)           ?
?  - PbrMaterial, CustomMaterial              ?
?  - MaterialProperties (Observable)          ?
?  - MaterialFactory (Registration)           ?
???????????????????????????????????????????????
               ?
               ?
???????????????????????????????????????????????
?  Shader Builder Layer (Integration)         ?
?  - MaterialShaderBuilder                    ?
?  - Automatic code generation                ?
?  - Define management                        ?
???????????????????????????????????????????????
               ?
               ?
???????????????????????????????????????????????
?  Shader Library Layer (Low-Level)           ?
?  - ShaderCompiler (Preprocessing)           ?
?  - GlslHeaders (Headers, PBR functions)     ?
?  - Caching system                           ?
???????????????????????????????????????????????
```

---

## Quick Start

### 1. Basic PBR Material

```csharp
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Graphics;

// Create a PBR material
var material = new PbrMaterial();
material.Properties.Variables = new PBRMaterial
{
    Albedo = new Vector3(0.8f, 0.2f, 0.1f),
    Metallic = 0.5f,
    Roughness = 0.3f,
    Ao = 1.0f,
    Opacity = 1.0f
};

// Initialize pipeline - shaders generated automatically
var pipelineDesc = new RenderPipelineDesc
{
    Topology = Topology.Triangle,
    CullMode = CullMode.Back,
};
pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

material.InitializePipeline(context, pipelineDesc);

// Use the material
var pipeline = material.Pipeline;
cmdBuffer.BindRenderPipeline(pipeline);
```

### 2. Textured PBR Material

```csharp
var material = new PbrMaterial();

// Assign textures
material.Properties.BaseColorTexture = albedoTexture;
material.Properties.NormalTexture = normalTexture;
material.Properties.BaseColorSampler = sampler;

// Shader automatically enables texture sampling
material.InitializePipeline(context, pipelineDesc);
```

### 3. Custom Shader Material

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithDefine("USE_CUSTOM_LIGHTING")
    .WithCustomCode(@"
        vec3 customLighting(PBRMaterial mat, vec3 viewDir) {
            return mat.albedo * dot(mat.normal, viewDir);
        }
    ")
    .WithCustomMain(@"
        void main() {
            PBRMaterial material;
            material.albedo = vec3(0.8, 0.2, 0.2);
            material.normal = normalize(fragNormal);
            
            vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
            vec3 color = customLighting(material, viewDir);
            
            outColor = vec4(color, 1.0);
        }
    ");

var result = builder.BuildMaterialPipeline(context, "CustomMaterial");
```

---

## Core Components

### MaterialShaderBuilder

The central class for building shaders from materials.

```csharp
public class MaterialShaderBuilder
{
    // Enable/disable PBR
    public MaterialShaderBuilder WithPBRShading(bool enable);
    
    // Add preprocessor defines
    public MaterialShaderBuilder WithDefine(string name, string? value = null);
    
    // Add custom GLSL code
    public MaterialShaderBuilder WithCustomCode(string glslCode);
    
    // Custom main function
    public MaterialShaderBuilder WithCustomMain(string fragmentMain);
    
    // Auto-configure from material
    public MaterialShaderBuilder ForMaterial(PbrMaterialProperties material);
    
    // Build fragment shader only
    public ShaderBuildResult BuildFragmentShader();
    
    // Build complete pipeline (vertex + fragment)
    public MaterialShaderResult BuildMaterialPipeline(IContext context, string? debugName);
}
```

### PbrMaterial

Concrete material implementation with automatic shader generation.

```csharp
public class PbrMaterial : Material<PbrMaterialProperties>
{
    // Cached pipeline (lazily created)
    public override RenderPipelineResource Pipeline { get; }
    
    // Initialize/update pipeline
    public bool InitializePipeline(IContext context, RenderPipelineDesc pipelineDesc);
    
    // Invalidate cached pipeline
    public void InvalidatePipeline();
}
```

### MaterialFactory Integration

Materials auto-register with the factory:

```csharp
// Create by name
var material = MaterialFactory.Create("PBR");

// List available materials
var materials = MaterialFactory.GetRegisteredKeys();
```

---

## Feature Matrix

| Feature | Automatic | Manual Builder | Custom Shader |
|---------|-----------|----------------|---------------|
| Basic PBR | ? | ? | ? |
| Texture Support | ? | ? | ? |
| Normal Mapping | ? | ? | ? |
| Custom Lighting | ? | ? | ? |
| Shader Variants | ? | ? | ? |
| Property Binding | ? | ?? | ? |
| Hot Reload | ? | ? | ? |

Legend:
- ? Fully supported
- ?? Partially supported
- ? Not supported

---

## Common Patterns

### Pattern 1: Material Library

Create a reusable library of materials:

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
            Roughness = 0.2f,
            Ao = 1.0f
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
            Roughness = 0.5f,
            Ao = 1.0f
        };
        return material;
    }
}
```

### Pattern 2: Quality Levels

Generate shader variants for different quality settings:

```csharp
public enum MaterialQuality { Low, Medium, High }

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

### Pattern 3: Dynamic Updates

Handle runtime property changes:

```csharp
public class DynamicMaterial
{
    private PbrMaterial _material;
    private IContext _context;
    private RenderPipelineDesc _pipelineDesc;
    
    public void UpdateTextures(TextureResource? newTexture)
    {
        bool hadTexture = _material.Properties.BaseColorTexture.Valid;
        _material.Properties.BaseColorTexture = newTexture ?? TextureResource.Null;
        bool hasTexture = _material.Properties.BaseColorTexture.Valid;
        
        // Rebuild pipeline if texture state changed
        if (hadTexture != hasTexture)
        {
            _material.InvalidatePipeline();
            _material.InitializePipeline(_context, _pipelineDesc);
        }
    }
}
```

---

## Advanced Topics

### Custom Material Types

Create your own material types:

```csharp
[MaterialName("Toon")]
public class ToonMaterial : Material<ToonMaterialProperties>
{
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
        // Store pipeline...
        return result.Success;
    }
}
```

### Shader Injection Points

The MaterialShaderBuilder generates shaders with these sections:

1. **Header** - Version, extensions, headers
2. **Inputs** - Vertex attributes (position, normal, texcoord, tangent)
3. **Outputs** - Fragment color output
4. **Push Constants** - Material parameters
5. **Bindless Resources** - Texture arrays, sampler arrays
6. **Custom Code** - User-provided functions
7. **Main Function** - Either default PBR or custom

You can replace any section using the builder API.

### Performance Considerations

1. **Shader Caching**: The `ShaderCompiler` caches compiled shaders
2. **Pipeline Reuse**: Materials cache their pipelines
3. **Batch by Material**: Group draw calls by material to minimize state changes
4. **Lazy Compilation**: Shaders compile only when `InitializePipeline` is called
5. **Invalidation**: Call `InvalidatePipeline()` only when necessary

---

## Integration with Existing Systems

### With Scene Graph

```csharp
public class MaterialNode : SceneNode
{
    public PbrMaterial Material { get; set; }
    
    public override void Render(ICommandBuffer cmdBuffer)
    {
        cmdBuffer.BindRenderPipeline(Material.Pipeline);
        // Upload material constants
        // Draw geometry
    }
}
```

### With Render System

```csharp
public class MaterialRenderPass
{
    private Dictionary<Material, List<RenderItem>> _materialBatches = new();
    
    public void AddItem(RenderItem item, Material material)
    {
        if (!_materialBatches.ContainsKey(material))
            _materialBatches[material] = new List<RenderItem>();
        _materialBatches[material].Add(item);
    }
    
    public void Render(ICommandBuffer cmdBuffer)
    {
        foreach (var (material, items) in _materialBatches)
        {
            cmdBuffer.BindRenderPipeline(material.Pipeline);
            foreach (var item in items)
            {
                // Upload item-specific data
                // Draw
            }
        }
    }
}
```

---

## Comparison with Other Approaches

### Approach 1: Manual Shader Writing (Traditional)

```csharp
// ? Manual approach - brittle and error-prone
var vertexCode = File.ReadAllText("vertex.glsl");
var fragmentCode = File.ReadAllText("fragment.glsl");
var vertexShader = context.CreateShaderModuleGlsl(vertexCode, ShaderStage.Vertex);
var fragmentShader = context.CreateShaderModuleGlsl(fragmentCode, ShaderStage.Fragment);
```

**Problems:**
- No connection to material properties
- Manual synchronization required
- No automatic texture support
- Difficult to create variants

### Approach 2: Integrated System (This Implementation)

```csharp
// ? Integrated approach - automatic and type-safe
var material = new PbrMaterial();
material.Properties.BaseColorTexture = texture;
material.InitializePipeline(context, pipelineDesc);
```

**Advantages:**
- Automatic shader generation
- Type-safe material properties
- Texture support auto-configured
- Easy to create variants

---

## Best Practices

### DO ?

1. **Use MaterialShaderBuilder** for shader generation
2. **Cache pipelines** through the Material.Pipeline property
3. **Batch by material** to minimize state changes
4. **Register custom materials** with MaterialFactory
5. **Use defines** for shader variants
6. **Invalidate pipeline** when textures change significantly

### DON'T ?

1. **Don't manually write shaders** when MaterialShaderBuilder can generate them
2. **Don't recreate pipelines** every frame
3. **Don't skip caching** (use global cache by default)
4. **Don't mix manual and automatic** shader generation
5. **Don't forget error handling** when building shaders

---

## Troubleshooting

### Problem: Pipeline is null/invalid

**Solution**: Check if `InitializePipeline` returned true. Check build result errors:

```csharp
if (!material.InitializePipeline(context, pipelineDesc))
{
    // Check shader build errors
    Console.WriteLine("Pipeline creation failed");
}
```

### Problem: Textures not working

**Solution**: Ensure textures are assigned AND sampler is valid:

```csharp
material.Properties.BaseColorTexture = texture;
material.Properties.BaseColorSampler = sampler;  // Don't forget!
material.InvalidatePipeline();  // Rebuild with texture support
material.InitializePipeline(context, pipelineDesc);
```

### Problem: Custom shader not working

**Solution**: Verify GLSL syntax and included functions:

```csharp
var result = builder.BuildFragmentShader();
if (!result.Success)
{
    foreach (var error in result.Errors)
        Console.WriteLine(error);
}
```

### Problem: Performance issues

**Solution**: 
- Ensure pipeline caching is enabled
- Batch by material type
- Use the global shader cache
- Profile with GPU timestamp queries

---

## Examples

See `MaterialShaderIntegrationExamples.cs` for complete working examples:

1. **Example1** - Basic PBR material
2. **Example2** - Textured PBR material
3. **Example3** - Custom material shader
4. **Example4** - MaterialFactory usage
5. **Example5** - Material library pattern
6. **Example6** - Dynamic material updates
7. **Example7** - Conditional shader generation
8. **Example8** - Quality variants

---

## API Reference

### MaterialShaderBuilder

| Method | Description |
|--------|-------------|
| `WithPBRShading(bool)` | Enable/disable PBR shading functions |
| `WithDefine(string, string?)` | Add preprocessor define |
| `WithCustomCode(string)` | Add custom GLSL functions |
| `WithCustomMain(string)` | Replace main function |
| `ForMaterial(PbrMaterialProperties)` | Auto-configure from material |
| `BuildFragmentShader()` | Build fragment shader only |
| `BuildMaterialPipeline(IContext, string?)` | Build complete pipeline |

### PbrMaterial

| Member | Description |
|--------|-------------|
| `Properties` | Material properties (albedo, metallic, etc.) |
| `Pipeline` | Cached render pipeline |
| `InitializePipeline(IContext, RenderPipelineDesc)` | Create/update pipeline |
| `InvalidatePipeline()` | Clear cached pipeline |

---

## Future Enhancements

Planned features:

- **Material variants** - LOD-based shader simplification
- **Hot reload** - Edit shaders at runtime
- **Visual editor** - Node-based material editor
- **Shader graph** - Visual shader programming
- **Material instancing** - Efficient parameter updates
- **Uber shader** - Single shader with many branches

---

## Contributing

When adding new material types or shader features:

1. Follow existing naming conventions
2. Add examples to `MaterialShaderIntegrationExamples.cs`
3. Register materials with `MaterialFactory`
4. Document shader injection points
5. Test with both textured and non-textured variants

---

## License

MIT License - Same as HelixToolkit.Nex project

---

**Version**: 1.0.0  
**Created for**: HelixToolkit.Nex  
**Last Updated**: 2024
