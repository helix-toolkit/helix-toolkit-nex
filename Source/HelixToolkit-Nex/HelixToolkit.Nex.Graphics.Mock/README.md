# HelixToolkit.Nex.Graphics.Mock

A mock implementation of the HelixToolkit.Nex.Graphics context and command buffer for unit testing purposes.

## Overview

This library provides lightweight, in-memory mock implementations of the graphics API interfaces, enabling unit tests without requiring actual GPU resources or Vulkan drivers.

## Features

- **MockContext**: Full implementation of `IContext` interface with in-memory resource tracking
- **MockCommandBuffer**: Full implementation of `ICommandBuffer` interface with command recording tracking
- **No GPU Required**: All operations are mocked and run entirely in CPU memory
- **State Tracking**: Tracks all resource creations, uploads, downloads, and command buffer operations
- **Validation Support**: Provides access to recorded operations for test assertions

## Usage

### Basic Example

```csharp
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;

// Create a mock context
using var context = new MockContext();
context.Initialize();

// Create resources
var bufferDesc = new BufferDesc(
    BufferUsageBits.Vertex,
    StorageType.Device,
    IntPtr.Zero,
    1024
);
context.CreateBuffer(bufferDesc, out var buffer, "TestBuffer");

// Acquire command buffer
var cmdBuffer = context.AcquireCommandBuffer();
var mockCmdBuffer = (MockCommandBuffer)cmdBuffer;

// Record commands
cmdBuffer.Draw(3, 1, 0, 0);

// Verify recorded commands
Assert.Contains("Draw(vertices=3, instances=1)", mockCmdBuffer.RecordedCommands);

// Submit
var submitHandle = context.Submit(cmdBuffer, TextureHandle.Null);

// Verify submission
Assert.Single(context.SubmittedCommands);
```

### Testing Resource Creation

```csharp
[Fact]
public void TestBufferCreation()
{
    using var context = new MockContext();
    context.Initialize();

    // Create a buffer with initial data
    var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
    var buffer = context.CreateBuffer(
        data,
        BufferUsageBits.Vertex | BufferUsageBits.Storage,
        StorageType.Device,
        "MyVertexBuffer"
    );

    Assert.True(buffer.Valid);
    
    // Download and verify
    var downloaded = new float[4];
    context.Download(buffer, out downloaded);
    Assert.Equal(data, downloaded);
}
```

### Testing Command Recording

```csharp
[Fact]
public void TestDrawCommands()
{
    using var context = new MockContext();
    context.Initialize();

    var cmdBuffer = (MockCommandBuffer)context.AcquireCommandBuffer();
    
    // Create render pass
    var renderPass = new RenderPass(new[]
    {
        new AttachmentDesc
        {
            Format = Format.BGRA_UN8,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store
        }
    });

    var framebuffer = new Framebuffer
    {
        Width = 1920,
        Height = 1080,
        Colors = new[]
        {
            new FramebufferAttachmentDesc
            {
                Texture = context.GetCurrentSwapchainTexture()
            }
        }
    };

    // Record rendering commands
    cmdBuffer.BeginRendering(renderPass, framebuffer, Dependencies.Empty);
    cmdBuffer.Draw(3, 1, 0, 0);
    cmdBuffer.EndRendering();

    // Verify recorded commands
    Assert.Contains("BeginRendering", cmdBuffer.RecordedCommands[0]);
    Assert.Contains("Draw", cmdBuffer.RecordedCommands[1]);
    Assert.Contains("EndRendering", cmdBuffer.RecordedCommands[2]);
}
```

### Testing Upload/Download Operations

```csharp
[Fact]
public void TestBufferUploadDownload()
{
    using var context = new MockContext();
    context.Initialize();

    // Create buffer
    var bufferDesc = new BufferDesc(
        BufferUsageBits.Storage,
        StorageType.Device,
        IntPtr.Zero,
        sizeof(int) * 100
    );
    context.CreateBuffer(bufferDesc, out var buffer);

    // Upload data
    var uploadData = Enumerable.Range(0, 100).ToArray();
    context.Upload(buffer, 0, in uploadData);

    // Download and verify
    var downloadData = new int[100];
    context.Download(buffer, out downloadData);
    Assert.Equal(uploadData, downloadData);
}
```

## MockContext Properties

### Configurable Properties

- `SwapchainFormat`: Set the mock swapchain format (default: `Format.BGRA_UN8`)
- `SwapchainColorSpace`: Set the color space (default: `ColorSpace.SRGB_NONLINEAR`)
- `NumSwapchainImages`: Number of swapchain images (default: `3`)
- `MaxStorageBufferRange`: Maximum storage buffer range (default: `128MB`)
- `FramebufferMSAABitMask`: MSAA support bit mask (default: `0x7F`)
- `TimestampPeriodMs`: GPU timestamp period (default: `0.000001`)

### Tracking Properties

- `AcquiredCommandBuffers`: List of all acquired command buffers
- `SubmittedCommands`: List of all submitted commands with handles

## MockCommandBuffer Properties

### Command Tracking

- `RecordedCommands`: Read-only list of all recorded command names
- `IsRendering`: Whether a render pass is currently active
- `IsSubmitted`: Whether this command buffer has been submitted
- `IsSecondary`: Whether this is a secondary command buffer

## Supported Operations

### Resource Creation
- ✅ Buffers
- ✅ Textures
- ✅ Texture Views
- ✅ Samplers
- ✅ Shader Modules
- ✅ Compute Pipelines
- ✅ Render Pipelines
- ✅ Query Pools

### Buffer Operations
- ✅ Upload (with generic support)
- ✅ Download (with generic support)
- ✅ Mapped memory access
- ✅ GPU address queries

### Texture Operations
- ✅ Upload
- ✅ Download
- ✅ Dimension queries
- ✅ Format queries
- ✅ Aspect ratio queries

### Command Buffer Operations
- ✅ Draw commands (indexed, indirect, instanced)
- ✅ Compute dispatch
- ✅ Mesh shading
- ✅ Resource transitions
- ✅ Buffer updates and copies
- ✅ Texture operations
- ✅ Debug markers
- ✅ Query operations
- ✅ Pipeline binding
- ✅ Viewport and scissor

### Swapchain Operations
- ✅ Get current texture
- ✅ Get format and color space
- ✅ Get image count and index
- ✅ Recreate swapchain

## Limitations

- **No Validation**: The mock does not perform deep validation like a real GPU driver
- **No Execution**: Commands are only recorded, not executed
- **No Synchronization**: All wait operations complete immediately
- **Memory is CPU-side**: All "GPU" memory is actually heap-allocated arrays

## Integration with Test Frameworks

### xUnit Example

```csharp
public class GraphicsTests : IDisposable
{
    private readonly MockContext _context;

    public GraphicsTests()
    {
        _context = new MockContext();
        _context.Initialize();
    }

    [Fact]
    public void TestResourceCreation()
    {
        // Your test code here
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
```

### NUnit Example

```csharp
[TestFixture]
public class GraphicsTests
{
    private MockContext? _context;

    [SetUp]
    public void Setup()
    {
        _context = new MockContext();
        _context.Initialize();
    }

    [Test]
    public void TestResourceCreation()
    {
        // Your test code here
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }
}
```

## Best Practices

1. **Always Initialize**: Call `Initialize()` before using the context
2. **Dispose Properly**: Use `using` statements or explicit disposal
3. **Verify Operations**: Check `RecordedCommands` to validate command sequences
4. **Configure Before Use**: Set mock properties before initialization if needed
5. **Use Type Casting**: Cast `ICommandBuffer` to `MockCommandBuffer` to access tracking data

## Thread Safety

- MockContext uses `ConcurrentDictionary` for resource storage
- Resource handle allocation is thread-safe
- Command buffer recording is **not** thread-safe (matches real API behavior)

## Target Framework

- **.NET 8.0**

## Dependencies

- HelixToolkit.Nex.Graphics
- HelixToolkit.Nex
- HelixToolkit.Nex.Maths

## License

MIT License - See LICENSE file in the root of the repository for details.
