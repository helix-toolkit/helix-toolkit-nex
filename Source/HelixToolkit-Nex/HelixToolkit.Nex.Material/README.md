# Material Type System

## Overview

The Material Type System provides a powerful way to create **uber shaders** that contain multiple material implementations, selectable at runtime through specialization constants. This approach reduces shader compilation overhead while maintaining flexibility.

## Key Concepts

### Material Type Registry

The `MaterialTypeRegistry` is a global registry that maps material type names to unique IDs and their GLSL implementations. Each registered material type defines:

- **Type ID**: Unique identifier (uint) used in specialization constants
- **Name**: Human-readable name (e.g., "PBR", "Unlit", "Toon")
- **Output Color Implementation**: GLSL code for the `outputColor()` function
- **Material Creation Implementation** (optional): Custom `createPBRMaterial()` logic
- **Additional Code** (optional): Helper functions and utilities

### Uber Shaders

An **uber shader** contains all registered material type implementations in a single shader. The actual material type is selected at pipeline creation time using a specialization constant (`materialType`, constant ID 0).

**Benefits:**
- ✅ Compile shaders once, use for multiple material types
- ✅ Efficient runtime switching between material types
- ✅ Reduced shader variant explosion
- ✅ Better pipeline caching

**Trade-offs:**
- ⚠️ Larger shader code (but GPU drivers optimize out unused branches)
- ⚠️ Requires specialization constant support

## Built-in Material Types

| Type ID | Name | Description |
|---------|------|-------------|
| 0 | PBR | Physically-based rendering with Forward+ lighting |
| 1 | Unlit | Simple unlit shading (albedo + emissive) |
| 2 | DebugTileLightCount | Visualize Forward+ tile light counts |
| 3 | NormalViz | Visualize surface normals |

## Quick Start

### 1. Build an Uber Shader

```csharp
// Create builder (uber shader is default)
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithUberShader(true);

// Build shader modules
var result = builder.BuildMaterialPipeline(context, "UberShader");

if (!result.Success)
{
    // Handle errors
    foreach (var error in result.Errors)
        Console.WriteLine(error);
    return;
}

// result.VertexShader and result.FragmentShader can be reused
// for multiple pipelines with different material types
```

### 2. Create Pipelines with Different Material Types

```csharp
// PBR material pipeline
var pbrPipelineDesc = new RenderPipelineDesc
{
    Topology = Topology.Triangle,
    CullMode = CullMode.Back,
    VertexShader = result.VertexShader,
    FragementShader = result.FragmentShader,
};
pbrPipelineDesc.Colors[0].Format = Format.RGBA_UN8;
pbrPipelineDesc.SetMaterialType("PBR"); // Use extension method

var pbrMaterial = new Material();
pbrMaterial.CreatePipeline(context, pbrPipelineDesc);

// Unlit material pipeline (reuses same shaders!)
var unlitPipelineDesc = new RenderPipelineDesc
{
    Topology = Topology.Triangle,
    CullMode = CullMode.Back,
    VertexShader = result.VertexShader,
    FragementShader = result.FragmentShader,
};
unlitPipelineDesc.Colors[0].Format = Format.RGBA_UN8;
unlitPipelineDesc.SetMaterialType("Unlit");

var unlitMaterial = new Material();
unlitMaterial.CreatePipeline(context, unlitPipelineDesc);
```

### 3. Register Custom Material Types

```csharp
// Register a toon shading material
var toonId = MaterialTypeRegistry.Register(
    name: "Toon",
    outputColorImpl: @"
    PBRMaterial material = createPBRMaterial();
    vec3 normal = material.normal;
    vec3 viewDir = normalize(fpConst.cameraPosition - fragWorldPos);
    
    // Toon shading steps
    float intensity = max(0.0, dot(normal, -viewDir));
    vec3 color;
    if (intensity > 0.95) 
        color = material.albedo;
    else if (intensity > 0.5) 
        color = material.albedo * 0.6;
    else if (intensity > 0.25) 
        color = material.albedo * 0.4;
    else 
        color = material.albedo * 0.2;
    
    finalColor = vec4(color + material.emissive, material.opacity);
    return;",
    additionalCode: @"
    // Optional helper functions
    vec3 toonStep(vec3 color, float intensity, int levels) {
        float step = 1.0 / float(levels);
        float level = floor(intensity / step) * step;
        return color * level;
    }"
);

Console.WriteLine($"Registered Toon material with ID: {toonId}");

// Rebuild uber shader to include new material type
result = builder.BuildMaterialPipeline(context, "UberShader_WithToon");
```

## Advanced Usage

### Single Material Type Shader

For optimization, you can compile a shader for only one material type:

```csharp
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithMaterialType("PBR"); // Specify exact type

var result = builder.BuildMaterialPipeline(context, "PBR_Only");

// This shader only contains PBR code, smaller binary
```

### Custom Material Creation Logic

Override how materials are constructed from properties:

```csharp
MaterialTypeRegistry.Register(new MaterialTypeRegistration
{
    TypeId = 100,
    Name = "CustomPBR",
    CreateMaterialImplementation = @"
    PBRProperties props = getPBRMaterial();
    PBRMaterial material;
    
    // Custom processing
    material.albedo = pow(props.albedo, vec3(2.2)); // sRGB to linear
    material.roughness = props.roughness * props.roughness; // Squared
    material.metallic = props.metallic;
    material.ao = props.ao;
    material.emissive = props.emissive;
    material.opacity = props.opacity;
    material.ambient = props.ambient;
    material.normal = normalize(fragNormal);
    
    return material;",
    OutputColorImplementation = @"
    PBRMaterial material = createPBRMaterial();
    forwardPlusLighting(material, finalColor);
    return;"
});
```

### Material Type Introspection

```csharp
// List all registered types
foreach (var reg in MaterialTypeRegistry.GetAllRegistrations())
{
    Console.WriteLine($"{reg.TypeId}: {reg.Name}");
}

// Get type by name
var typeId = MaterialTypeRegistry.GetTypeId("PBR");

// Get type by ID
if (MaterialTypeRegistry.TryGetById(typeId.Value, out var registration))
{
    Console.WriteLine($"Found: {registration.Name}");
}
```

## API Reference

### MaterialShaderBuilder

| Method | Description |
|--------|-------------|
| `WithUberShader(bool)` | Build uber shader with all material types (default) |
| `WithMaterialType(string)` | Build shader for specific material type only |
| `WithMaterialType(uint)` | Build shader for specific material type ID only |
| `BuildMaterialPipeline(context, name)` | Build vertex and fragment shaders |

### MaterialTypeRegistry

| Method | Description |
|--------|-------------|
| `Register(registration)` | Register a material type with full control |
| `Register(name, outputImpl, ...)` | Register with auto-assigned ID |
| `TryGetByName(name, out reg)` | Get registration by name |
| `TryGetById(id, out reg)` | Get registration by ID |
| `GetTypeId(name)` | Get ID for a type name |
| `GetTypeName(id)` | Get name for a type ID |
| `GetAllRegistrations()` | Get all registered types |

### Extension Methods (RenderPipelineDesc)

| Method | Description |
|--------|-------------|
| `SetMaterialType(string)` | Set material type by name |
| `SetMaterialType(uint)` | Set material type by ID |
| `GetMaterialType()` | Get current material type ID |

## Best Practices

### ✅ DO

1. **Use uber shaders for dynamic content** - When you need to switch between material types frequently
2. **Register material types at startup** - Before building shaders
3. **Cache shader compilation results** - Reuse `MaterialShaderResult` across multiple pipelines
4. **Use meaningful type names** - Makes debugging easier
5. **Provide helper functions** - Use `AdditionalCode` for shared utilities

### ❌ DON'T

1. **Don't rebuild shaders every frame** - Very expensive
2. **Don't register duplicate type IDs** - Will throw exception
3. **Don't modify registry during shader compilation** - Thread safety
4. **Don't forget to set material type** - Pipeline will use default (ID 0)
5. **Don't mix uber and single-type approaches** - Pick one strategy

## Performance Considerations

### Specialization Constants

Specialization constants allow the driver to optimize out unused branches:

```glsl
// This code:
if (materialType == 0u) {
    // PBR code
} else if (materialType == 1u) {
    // Unlit code
}

// Becomes this after specialization (for materialType = 0):
// PBR code
```

**Result**: No runtime branching overhead!

### Shader Caching

The shader compiler includes a global cache:

```csharp
// First build - compiles shader
var result1 = builder.BuildMaterialPipeline(context, "Uber");

// Second build with same source - uses cache
var result2 = builder.BuildMaterialPipeline(context, "Uber");
```

### Pipeline State Objects (PSO)

Create PSOs ahead of time to avoid hitches:

```csharp
// Startup: Pre-create common material pipelines
var materialTypes = new[] { "PBR", "Unlit", "Toon" };
var pipelines = new Dictionary<string, Material>();

var uberShader = builder.BuildMaterialPipeline(context, "Uber");

foreach (var type in materialTypes)
{
    var desc = new RenderPipelineDesc { ... };
    desc.SetMaterialType(type);
    
    var material = new Material();
    material.CreatePipeline(context, desc);
    pipelines[type] = material;
}

// Runtime: Instant switching
cmdBuffer.BindRenderPipeline(pipelines["PBR"]);
```

## Shader Template Structure

The uber shader generation modifies the `psPBRTemplate.glsl` template:

```glsl
// Template has this structure:
// 1. Includes and buffers
// 2. Helper functions (forwardPlusLighting, debugTileLighting, etc.)
// 3. createPBRMaterial() - Can be overridden per type
// 4. outputColor() - GENERATED from material types
// 5. main() - Calls outputColor()

// Generated outputColor() function:
layout (constant_id = 0) const uint materialType = 0;

void outputColor(out vec4 finalColor)
{
    if (materialType == 0u) {
        // PBR implementation
        PBRMaterial material = createPBRMaterial();
        forwardPlusLighting(material, finalColor);
        return;
    } else if (materialType == 1u) {
        // Unlit implementation
        PBRMaterial material = createPBRMaterial();
        nonLitOutputColor(material, finalColor);
        return;
    }
    // ... more types ...
    
    // Fallback
    finalColor = vec4(1.0, 0.0, 1.0, 1.0); // Magenta
}
```

## Migration Guide

### From Old System

**Before:**
```csharp
// Had to manually manage shader variants
var pbrBuilder = new MaterialShaderBuilder()
    .WithDefine("USE_PBR")
    .WithCustomMain(...);

var unlitBuilder = new MaterialShaderBuilder()
    .WithDefine("USE_UNLIT")
    .WithCustomMain(...);
    
// Compile two separate shaders
```

**After:**
```csharp
// Single uber shader for all types
var builder = new MaterialShaderBuilder()
    .WithUberShader(true);

var result = builder.BuildMaterialPipeline(context, "Uber");

// Create pipelines with different types
pipelineDesc.SetMaterialType("PBR");
unlitPipelineDesc.SetMaterialType("Unlit");
```

## Examples

See `MaterialTypeExamples.cs` for complete working examples:

- `Example1_UberShader` - Basic uber shader
- `Example2_CreatePBRMaterial` - Create specific material from uber shader
- `Example3_MultipleVariants` - Multiple materials from one shader
- `Example4_RegisterCustomMaterialType` - Register custom type
- `Example5_SingleMaterialType` - Optimized single-type shader
- `Example8_CompleteWorkflow` - End-to-end example

## Troubleshooting

### Material appears magenta
- Material type not registered or ID mismatch
- Check: `MaterialTypeRegistry.GetTypeId(typeName)`

### Shader compilation fails
- GLSL syntax error in material implementation
- Check: `result.Errors` list
- Verify all required functions are available in template

### Wrong material type rendered
- Specialization constant not set
- Check: `desc.GetMaterialType()` returns expected ID
- Ensure `SetMaterialType()` called before pipeline creation

## Future Enhancements

Potential improvements:

- [ ] Material type validation during registration
- [ ] Automatic dependency tracking for helper functions
- [ ] Visual material type editor
- [ ] Hot-reload support for custom material types
- [ ] Performance profiling per material type

## See Also

- [MATERIAL_SHADER_INTEGRATION.md](MATERIAL_SHADER_INTEGRATION.md) - Material system overview
- [FORWARD_PLUS_GUIDE.md](FORWARD_PLUS_GUIDE.md) - Forward+ rendering
- `MaterialTypeExamples.cs` - Working code examples
- `psPBRTemplate.glsl` - Shader template structure
