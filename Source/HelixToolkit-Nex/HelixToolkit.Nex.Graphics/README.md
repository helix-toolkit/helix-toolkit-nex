# HelixToolkit.Nex.Graphics

A modern, backend-agnostic graphics abstraction library for .NET 8 that provides a unified API for GPU rendering operations. This library serves as the foundational graphics layer for the HelixToolkit.Nex rendering framework.

## Overview

HelixToolkit.Nex.Graphics is a low-level graphics abstraction library that provides:

- **Backend-Agnostic API**: Unified interface that can support multiple graphics backends (currently Vulkan via HelixToolkit.Nex.Graphics.Vulkan)
- **Modern Graphics Features**: Support for compute shaders, mesh shaders, ray tracing, and advanced rendering techniques
- **Resource Management**: Automatic reference-counted resource lifetime management
- **Command Buffer Recording**: Efficient GPU command recording and submission
- **Type Safety**: Strongly-typed handles and descriptors for all GPU resources
- **Performance**: Zero-cost abstractions with minimal overhead

## Target Framework

- **.NET 8.0** - Requires the latest .NET runtime

## Key Features

### Resource Types

- **Buffers**: Vertex, index, uniform, and storage buffers with flexible memory types
- **Textures**: 2D, 3D, and cube map textures with mipmap support
- **Samplers**: Configurable texture sampling with filtering and wrapping modes
- **Pipelines**: Render and compute pipelines with full shader stage support
- **Shader Modules**: GLSL and SPIR-V shader compilation
- **Query Pools**: GPU timestamp and performance queries
- **Framebuffers**: Multi-attachment render targets with MSAA support

### Rendering Capabilities

- **Modern Shader Stages**: Vertex, Fragment, Compute, Geometry, Tessellation, Mesh, and Task shaders
- **Advanced Rendering**: Multisampling (MSAA), depth/stencil testing, blending, culling
- **Indirect Drawing**: GPU-driven rendering with indirect draw and dispatch
- **Mesh Shading**: Support for task and mesh shader pipelines
- **Flexible Vertex Input**: Customizable vertex layouts and formats
- **Push Constants**: Fast shader parameter updates without buffer binds

### Memory Management

- **Automatic Lifetime**: Reference-counted resources with automatic cleanup
- **Multiple Storage Types**: Device-local (VRAM), host-visible (CPU-accessible), and memoryless (transient)
- **Mapped Memory**: Direct CPU access to host-visible buffers
- **Upload/Download**: Convenient data transfer between CPU and GPU

## Architecture

### Core Interfaces

#### IContext

The main graphics context interface providing:
- Resource creation (buffers, textures, pipelines, etc.)
- Command buffer acquisition and submission
- Data upload/download operations
- Swapchain management
- Query pool operations

```csharp
public interface IContext : IInitializable
{
    ICommandBuffer AcquireCommandBuffer();
    SubmitHandle Submit(ICommandBuffer commandBuffer, in TextureHandle present);
    void Wait(in SubmitHandle handle);
    
    ResultCode CreateBuffer(in BufferDesc desc, out BufferResource buffer, string? debugName = null);
    ResultCode CreateTexture(in TextureDesc desc, out TextureResource texture, string? debugName = null);
    ResultCode CreateRenderPipeline(in RenderPipelineDesc desc, out RenderPipelineResource pipeline);
    // ... and more
}
```

#### ICommandBuffer

Command buffer interface for recording GPU operations:
- Debug markers and labels
- Pipeline binding (render and compute)
- Draw calls (indexed, indirect, instanced, mesh)
- Resource updates and transitions
- Compute dispatches
- Query operations

```csharp
public interface ICommandBuffer
{
    void BeginRendering(in RenderPass renderPass, in Framebuffer desc, in Dependencies deps);
    void BindRenderPipeline(in RenderPipelineHandle handle);
    void Draw(size_t vertexCount, size_t instanceCount = 1, ...);
    void DrawIndexed(size_t indexCount, size_t instanceCount = 1, ...);
    void DispatchThreadGroups(in Dimensions threadgroupCount, in Dependencies deps);
    void EndRendering();
    // ... and more
}
```

### Resource System

All GPU resources use reference-counted wrappers that automatically manage lifetime:

- `BufferResource` - GPU buffer wrapper
- `TextureResource` - Texture/image wrapper
- `SamplerResource` - Sampler state wrapper
- `ShaderModuleResource` - Compiled shader wrapper
- `RenderPipelineResource` - Render pipeline state wrapper
- `ComputePipelineResource` - Compute pipeline state wrapper
- `QueryPoolResource` - Query pool wrapper

Each resource type:
- Holds a strongly-typed handle
- References the owning `IContext`
- Automatically destroys the handle when disposed
- Provides a `Null` static instance for convenience

## Usage Examples

### Basic Triangle Rendering

```csharp
using HelixToolkit.Nex.Graphics;

// Initialize context (implementation-specific)
IContext context = ...; // e.g., VulkanContext

// Create vertex buffer
float[] vertices = new float[]
{
    // Position (x, y)     Color (r, g, b)
    -0.5f, -0.5f,        1.0f, 0.0f, 0.0f,
     0.5f, -0.5f,        0.0f, 1.0f, 0.0f,
     0.0f,  0.5f,        0.0f, 0.0f, 1.0f,
};

var vertexBuffer = context.CreateBuffer(
    vertices,
    BufferUsageBits.Vertex,
    StorageType.Device,
    debugName: "TriangleVertices"
);

// Create shaders
string vertexShader = @"
#version 460
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec3 inColor;
layout(location = 0) out vec3 fragColor;

void main() {
    gl_Position = vec4(inPosition, 0.0, 1.0);
    fragColor = inColor;
}";

string fragmentShader = @"
#version 460
layout(location = 0) in vec3 fragColor;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragColor, 1.0);
}";

var vertexModule = context.CreateShaderModuleGlsl(vertexShader, ShaderStage.Vertex);
var fragmentModule = context.CreateShaderModuleGlsl(fragmentShader, ShaderStage.Fragment);

// Create vertex input layout
var vertexInput = new VertexInput
{
    Bindings = new[]
    {
        new VertexInputBinding { Binding = 0, Stride = 5 * sizeof(float), InputRate = VertexInputRate.Vertex }
    },
    Attributes = new[]
    {
        new VertexAttribute { Location = 0, Binding = 0, Format = VertexFormat.Float2, Offset = 0 },
        new VertexAttribute { Location = 1, Binding = 0, Format = VertexFormat.Float3, Offset = 2 * sizeof(float) }
    }
};

// Create render pipeline
var pipelineDesc = new RenderPipelineDesc
{
    VertexShader = vertexModule,
    FragementShader = fragmentModule,
    VertexInput = vertexInput,
    Topology = Topology.Triangle,
    Colors = new[] { new ColorAttachment { Format = context.GetSwapchainFormat() } }
};

var pipeline = context.CreateRenderPipeline(pipelineDesc);

// Render loop
while (running)
{
    var cmd = context.AcquireCommandBuffer();
    
    // Begin render pass
    var renderPass = new RenderPass
    {
        ColorAttachments = new[]
        {
            new RenderPassColorAttachment
            {
                Texture = context.GetCurrentSwapchainTexture(),
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f)
            }
        }
    };
    
    var framebuffer = new Framebuffer
    {
        Width = swapchainWidth,
        Height = swapchainHeight
    };
    
    cmd.BeginRendering(renderPass, framebuffer, Dependencies.Empty);
    cmd.BindRenderPipeline(pipeline);
    cmd.BindViewport(new ViewportF(0, 0, swapchainWidth, swapchainHeight));
    cmd.BindScissorRect(new ScissorRect(0, 0, swapchainWidth, swapchainHeight));
    cmd.BindVertexBuffer(0, vertexBuffer);
    cmd.Draw(3);
    cmd.EndRendering();
    
    // Submit and present
    context.Submit(cmd, context.GetCurrentSwapchainTexture());
}
```

### Compute Shader Example

```csharp
// Create storage buffers
var inputBuffer = context.CreateBuffer(
    inputData,
    BufferUsageBits.Storage,
    StorageType.Device
);

var outputBuffer = context.CreateBuffer(
    new BufferDesc(BufferUsageBits.Storage, StorageType.Device, nint.Zero, outputSize),
    out var _
);

// Create compute shader
string computeShader = @"
#version 460
layout(local_size_x = 64) in;

layout(set = 0, binding = 0) readonly buffer InputBuffer {
    float data[];
} input;

layout(set = 0, binding = 1) writeonly buffer OutputBuffer {
    float data[];
} output;

void main() {
    uint idx = gl_GlobalInvocationID.x;
    output.data[idx] = input.data[idx] * 2.0;
}";

var computeModule = context.CreateShaderModuleGlsl(computeShader, ShaderStage.Compute);

// Create compute pipeline
var computePipeline = context.CreateComputePipeline(new ComputePipelineDesc
{
    Shader = computeModule
});

// Dispatch compute work
var cmd = context.AcquireCommandBuffer();
cmd.BindComputePipeline(computePipeline);
cmd.DispatchThreadGroups(new Dimensions(width: numElements / 64, 1, 1), Dependencies.Empty);

var submitHandle = context.Submit(cmd, TextureHandle.Null);
context.Wait(submitHandle);

// Read results
context.Download(outputBuffer, outputData, outputSize);
```

### Texture and Sampling

```csharp
// Create texture
var textureDesc = new TextureDesc
{
    Type = TextureType.Texture2D,
    Format = Format.RGBA_UN8,
    Width = 1024,
    Height = 1024,
    NumMipLevels = 1,
    Usage = TextureUsageBits.Sampled | TextureUsageBits.TransferDst
};

var texture = context.CreateTexture(textureDesc, debugName: "MyTexture");

// Upload texture data
var textureRange = new TextureRangeDesc
{
    MipLevel = 0,
    LayerStart = 0,
    NumLayers = 1
};

context.Upload(texture, textureRange, textureData, textureDataSize);

// Create sampler
var samplerDesc = new SamplerStateDesc
{
    MinFilter = SamplerFilter.Linear,
    MagFilter = SamplerFilter.Linear,
    MipMap = SamplerMip.Linear,
    WrapU = SamplerWrap.Repeat,
    WrapV = SamplerWrap.Repeat,
    MaxAnisotropic = 16
};

var sampler = context.CreateSampler(samplerDesc);

// Use in rendering
// ... bind texture and sampler in shader descriptors
```

### Resource Upload and Download

```csharp
// Upload to buffer
var data = new MyStruct { Field1 = 42, Field2 = 3.14f };
context.Upload(buffer, offset: 0, in data);

// Upload array
var array = new float[] { 1.0f, 2.0f, 3.0f };
using var pinnedData = array.Pin();
unsafe
{
    context.Upload(buffer, 0, (nint)pinnedData.Pointer, (uint)(array.Length * sizeof(float)));
}

// Download from buffer
context.Download(buffer, out MyStruct result);

// Mapped buffer access (for host-visible buffers)
var mappedPtr = context.GetMappedPtr(buffer);
unsafe
{
    var dataPtr = (float*)mappedPtr;
    dataPtr[0] = 1.0f;
    dataPtr[1] = 2.0f;
}
context.FlushMappedMemory(buffer, 0, 2 * sizeof(float));
```

### Debug Markers

```csharp
var cmd = context.AcquireCommandBuffer();

cmd.PushDebugGroupLabel("Scene Rendering", new Color4(0.0f, 1.0f, 0.0f, 1.0f));
{
    cmd.InsertDebugEventLabel("Draw Opaque", new Color4(1.0f, 1.0f, 1.0f, 1.0f));
    // ... draw opaque objects
    
    cmd.InsertDebugEventLabel("Draw Transparent", new Color4(0.5f, 0.5f, 1.0f, 1.0f));
    // ... draw transparent objects
}
cmd.PopDebugGroupLabel();
```

### GPU Timing

```csharp
// Create query pool
context.CreateQueryPool(numQueries: 2, out var queryPool, debugName: "FrameTimings");

var cmd = context.AcquireCommandBuffer();

// Reset queries
cmd.ResetQueryPool(queryPool, firstQuery: 0, queryCount: 2);

// Start timing
cmd.WriteTimestamp(queryPool, query: 0);

// ... GPU work ...

// End timing
cmd.WriteTimestamp(queryPool, query: 1);

context.Submit(cmd, TextureHandle.Null);
context.Wait(SubmitHandle.Null);

// Read results
var timestamps = new ulong[2];
if (context.GetQueryPoolResults(queryPool, 0, 2, sizeof(ulong) * 2, 
    Marshal.UnsafeAddrOfPinnedArrayElement(timestamps, 0), sizeof(ulong)))
{
    double timeMs = (timestamps[1] - timestamps[0]) * context.GetTimestampPeriodToMs();
    Console.WriteLine($"GPU time: {timeMs:F3} ms");
}
```

## Extension Methods

The library provides convenient extension methods in `ContextExtensions`:

```csharp
// Throw on error variants
var buffer = context.CreateBuffer(desc); // Throws if creation fails
var texture = context.CreateTexture(desc);
var pipeline = context.CreateRenderPipeline(desc);

// GLSL shader creation
var shader = context.CreateShaderModuleGlsl(glslSource, ShaderStage.Fragment, "MyShader");

// Simplified submission
context.Submit(commandBuffer); // No presentation
```

## Format Support

The library supports a wide range of texture and vertex formats:

### Texture Formats

- **Color**: R8, RG8, RGBA8, RGBA16F, RGBA32F, RGB10A2, BGR8, BGRA8
- **sRGB**: RGBA8_SRGB, BGRA8_SRGB
- **Depth/Stencil**: D16, D24, D32F, D24S8, D32FS8
- **Compressed**: ETC2_RGB8, ETC2_SRGB8, BC7_RGBA
- **YUV**: NV12, 420p

### Vertex Formats

- Float: Float1, Float2, Float3, Float4
- Integer: Byte1-4, UByte1-4, Short1-4, UShort1-4

Use `GetVertexFormatSize()` and texture format extension methods for format information.

## Best Practices

### Resource Management

1. **Use `using` statements**: Resources implement `IDisposable` for deterministic cleanup
   ```csharp
   using var buffer = context.CreateBuffer(desc);
   ```

2. **Null checks**: Use `resource.Valid` to check if a resource is valid
   ```csharp
   if (texture.Valid)
   {
       // Use texture
   }
   ```

3. **Debug names**: Always provide debug names for easier debugging
   ```csharp
   context.CreateBuffer(desc, debugName: "PlayerVertexBuffer");
   ```

### Performance

1. **Batch resource creation**: Create resources during initialization, not per-frame
2. **Reuse command buffers**: Acquire and submit command buffers efficiently
3. **Use appropriate storage types**: Device-local for GPU-only data, host-visible for frequent updates
4. **Minimize state changes**: Group draws by pipeline to reduce binding overhead
5. **Prefer mapped memory**: For frequently updated buffers, use host-visible storage with mapped pointers

### Synchronization

1. **Wait for completion**: Use `context.Wait(submitHandle)` when you need to ensure GPU work is done
2. **Device idle**: Pass `SubmitHandle.Null` to `Wait()` for full device synchronization (expensive!)
3. **Query before read**: Always wait for GPU completion before downloading results

## Constants and Limits

```csharp
// Maximum attachments
Constants.MAX_COLOR_ATTACHMENTS = 8

// Maximum mip levels
Constants.MAX_MIP_LEVELS = 16

// Device limits (query via context)
context.GetMaxStorageBufferRange();
context.GetFramebufferMSAABitMask();
```

## Error Handling

Most operations return `ResultCode` for error checking:

```csharp
var result = context.CreateBuffer(desc, out var buffer);
if (result != ResultCode.Success)
{
    // Handle error
}

// Or use extension methods that throw
try
{
    var buffer = context.CreateBuffer(desc);
}
catch (InvalidOperationException ex)
{
    // Handle error
}
```

## Dependencies

- **HelixToolkit.Nex** - Core utilities and base types
- **HelixToolkit.Nex.Maths** - Mathematics library for graphics operations

## Backend Implementations

- **HelixToolkit.Nex.Graphics.Vulkan** - Vulkan backend implementation

To use the library, reference both this package and a backend implementation:

```xml
<ItemGroup>
  <ProjectReference Include="..\HelixToolkit.Nex.Graphics\HelixToolkit.Nex.Graphics.csproj" />
  <ProjectReference Include="..\HelixToolkit.Nex.Graphics.Vulkan\HelixToolkit.Nex.Graphics.Vulkan.csproj" />
</ItemGroup>
```

## Integration with Other Libraries

### With HelixToolkit.Nex.Shaders

Combine with the shader building system for automatic header inclusion and preprocessing:

```csharp
using HelixToolkit.Nex.Shaders;

// Build and compile shader in one step
var (buildResult, shaderModule) = context.BuildAndCompileFragmentShaderWithPBR(
    shaderSource,
    debugName: "MyPBRShader"
);

if (buildResult.Success && shaderModule.Valid)
{
    // Use shader module in pipeline
}
```

See `HelixToolkit.Nex.Shaders\SHADER_BUILDING_README.md` for details.

## Thread Safety

- `IContext` is generally thread-safe for resource creation and destruction
- `ICommandBuffer` is **not** thread-safe; use one command buffer per thread
- Resource objects (e.g., `BufferResource`) are thread-safe for reference counting

## Platform Support

- Windows (x64, ARM64)
- Linux (x64, ARM64)
- macOS (x64, ARM64) - via MoltenVK

## Additional Resources

- **Samples**: See `Samples/HelloTriangle` for a minimal working example
- **Tests**: Refer to `HelixToolkit.Nex.Rendering.Tests` for usage patterns
- **Shader Building**: See `HelixToolkit.Nex.Shaders\SHADER_BUILDING_README.md`
- **Vulkan Documentation**: https://www.khronos.org/vulkan/

## License

MIT License - See LICENSE file for details

## Contributing

Contributions are welcome! Please follow the existing code style and add tests for new features.

---

**Note**: This library is part of the HelixToolkit.Nex framework and is designed to work seamlessly with other HelixToolkit.Nex components for building modern 3D graphics applications in .NET.
