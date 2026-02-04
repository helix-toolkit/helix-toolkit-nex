# Forward+ Rendering with Bindless Buffers

## Overview

The MaterialShaderBuilder now supports modern rendering techniques including:
- **Bindless vertex buffers** using `GL_EXT_buffer_reference`
- **Bindless light buffers** for efficient light data access
- **Forward+ rendering** with tile-based light culling

## Features

### Bindless Vertex Buffers

Instead of traditional vertex attributes, vertex data is fetched directly from GPU buffers using 64-bit addresses:

```csharp
var builder = new MaterialShaderBuilder()
    .WithBindlessVertices(true);
```

**Benefits:**
- Reduces vertex input state setup overhead
- Enables flexible vertex formats
- Simplifies mesh instancing
- Better GPU cache utilization

### Forward+ Rendering

Tile-based light culling that scales efficiently with many lights:

```csharp
var config = new ForwardPlusConfig
{
    TileSize = 16,              // 16x16 pixel tiles
    MaxLightsPerTile = 256,     // Max lights per tile
    UseComputeCulling = true    // Use compute shader for culling
};

var builder = new MaterialShaderBuilder()
    .WithForwardPlus(true, config);
```

**Benefits:**
- Handles thousands of lights efficiently
- Constant shading cost per pixel (unlike forward rendering)
- Lower memory usage than deferred rendering
- Works with MSAA and transparency

## Usage Example

### Basic Forward+ Setup

```csharp
// 1. Create material shader with Forward+
var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithForwardPlus(true, ForwardPlusConfig.Default);

var shaderResult = builder.BuildMaterialPipeline(context, "ForwardPlusPBR");

// 2. Create light buffer
var lights = new Light[]
{
    new Light
    {
        Position = new Vector3(0, 5, 0),
        Type = 1, // Point light
        Color = new Vector3(1, 1, 1),
        Intensity = 10.0f,
        Range = 20.0f
    },
    // ... more lights
};

var lightBuffer = context.CreateBuffer(
    lights,
    BufferUsageBits.Storage,
    StorageType.Device,
    "LightBuffer"
);

// 3. Run light culling compute shader
var cullingShader = ForwardPlusLightCulling.GenerateLightCullingComputeShader(config);
var cullingModule = context.CreateShaderModuleGlsl(cullingShader, ShaderStage.Compute);
var cullingPipeline = context.CreateComputePipeline(new ComputePipelineDesc
{
    ComputeShader = cullingModule
});

// 4. Dispatch culling
var tileCountX = (screenWidth + config.TileSize - 1) / config.TileSize;
var tileCountY = (screenHeight + config.TileSize - 1) / config.TileSize;
cmdBuffer.BindComputePipeline(cullingPipeline);
cmdBuffer.DispatchThreadGroups(new Dimensions(tileCountX, tileCountY, 1), Dependencies.Empty);

// 5. Render with Forward+
var constants = new ForwardPlusConstants
{
    ViewProjection = camera.ViewProjection,
    InverseViewProjection = Matrix4x4.Invert(camera.ViewProjection),
    CameraPosition = camera.Position,
    LightBufferAddress = context.GpuAddress(lightBuffer),
    LightCount = (uint)lights.Length,
    TileSize = config.TileSize,
    ScreenDimensions = new Vector2(screenWidth, screenHeight),
    TileCount = new Vector2(tileCountX, tileCountY)
};

cmdBuffer.BindRenderPipeline(shaderResult.FragmentShader);
cmdBuffer.PushConstants(constants);
cmdBuffer.Draw(vertexCount);
```

### Bindless Vertices Example

```csharp
// 1. Create vertex buffer with Vertex structure
var vertices = new Vertex[]
{
    new Vertex
    {
        Position = new Vector3(0, 0, 0),
        Normal = new Vector3(0, 1, 0),
        TexCoord = new Vector2(0, 0),
        Tangent = new Vector4(1, 0, 0, 1)
    },
    // ... more vertices
};

var vertexBuffer = context.CreateBuffer(
    vertices,
    BufferUsageBits.Storage,
    StorageType.Device,
    "BindlessVertexBuffer"
);

// 2. Create shader with bindless vertices
var builder = new MaterialShaderBuilder()
    .WithBindlessVertices(true)
    .WithForwardPlus(true);

var shaderResult = builder.BuildMaterialPipeline(context, "BindlessForwardPlus");

// 3. Render with bindless vertices
var constants = new ForwardPlusConstants
{
    // ... other fields
    VertexBufferAddress = context.GpuAddress(vertexBuffer)
};

// No vertex buffer binding needed!
cmdBuffer.BindRenderPipeline(shaderResult.FragmentShader);
cmdBuffer.PushConstants(constants);
cmdBuffer.Draw(vertexCount);
```

### Combined Example

```csharp
// Full Forward+ with bindless vertices and hundreds of lights
var config = new ForwardPlusConfig
{
    TileSize = 16,
    MaxLightsPerTile = 256
};

var builder = new MaterialShaderBuilder()
    .WithPBRShading(true)
    .WithBindlessVertices(true)
    .WithForwardPlus(true, config);

var material = new PbrMaterialProperties();
material.BaseColorTexture = albedoTexture;
material.MetallicRoughnessTexture = metallicRoughnessTexture;
material.NormalTexture = normalTexture;

builder.ForMaterial(material);

var shaderResult = builder.BuildMaterialPipeline(context, "FullForwardPlus");
```

## Performance Considerations

### Tile Size Selection

- **8x8**: Better culling precision, more compute overhead
- **16x16**: Balanced (recommended for most cases)
- **32x32**: Less compute overhead, coarser culling

### Max Lights Per Tile

- Affects shared memory usage in compute shader
- Typical values: 128-256 lights
- Higher values allow more lights but use more memory

### Light Culling Optimization

1. **Depth Prepass**: Helps establish tight depth bounds per tile
2. **Light Sorting**: Sort lights by size/importance before culling
3. **Hierarchical Culling**: Cull light groups first, then individual lights
4. **Async Compute**: Run light culling on async compute queue

## GPU Structure Alignment

All GPU structures use 16-byte alignment for optimal performance:

```csharp
// Vertex: 64 bytes (16-byte aligned)
Vertex.SizeInBytes == 64

// Light: 64 bytes (16-byte aligned)
Light.SizeInBytes == 64

// Tile: 8 bytes
LightGridTile.SizeInBytes == 8
```

## Limitations

- Requires Vulkan 1.3 or `GL_EXT_buffer_reference` extension
- Forward+ requires compute shader support
- Maximum lights per tile is fixed at compile time
- Transparent objects need special handling (render separately or use weighted OIT)

## Further Reading

- [Forward+ Rendering (Takahashi 2015)](https://takahiroharada.files.wordpress.com/2015/04/forward_plus.pdf)
- [Vulkan Buffer Device Address](https://www.khronos.org/blog/vulkan-1-2-buffer-device-addresses)
- [Tile-Based Forward Rendering](https://www.3dgep.com/forward-plus/)
