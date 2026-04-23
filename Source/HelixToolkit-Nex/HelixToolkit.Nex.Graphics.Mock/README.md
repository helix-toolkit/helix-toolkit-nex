```markdown
# HelixToolkit.Nex.Graphics.Mock

The `HelixToolkit.Nex.Graphics.Mock` package provides mock implementations of the `IContext` and `ICommandBuffer` interfaces for unit testing purposes. These mock classes allow developers to simulate and validate graphics API interactions without requiring actual GPU hardware, enabling efficient testing of rendering logic and resource management in isolation.

## Overview

The `HelixToolkit.Nex.Graphics.Mock` package is designed to facilitate unit testing of applications built with the HelixToolkit.Nex graphics engine. By providing mock implementations of key graphics interfaces, this package allows developers to:

- Simulate GPU resource creation and management.
- Record and validate command buffer operations.
- Test rendering workflows without requiring physical GPU access.
- Ensure correctness of API usage in a controlled environment.

This package is particularly useful for testing rendering pipelines, resource bindings, and command execution logic in scenarios where direct GPU access is not feasible or desirable.

## Key Types

| Type                  | Description                                                                 |
|-----------------------|-----------------------------------------------------------------------------|
| `MockContext`         | Mock implementation of the `IContext` interface for managing GPU resources.|
| `MockCommandBuffer`   | Mock implementation of the `ICommandBuffer` interface for recording and validating rendering commands. |

## Usage Examples

### Creating and Initializing a Mock Context

```csharp
using HelixToolkit.Nex.Graphics.Mock;

var mockContext = new MockContext();
var result = mockContext.Initialize();

if (result == ResultCode.Ok)
{
    Console.WriteLine("Mock context initialized successfully.");
}
else
{
    Console.WriteLine("Failed to initialize mock context.");
}
```

### Acquiring and Using a Mock Command Buffer

```csharp
// Acquire a primary command buffer
var commandBuffer = mockContext.AcquireCommandBuffer();

// Begin a rendering pass
var renderPass = new RenderPass(); // Assume a valid RenderPass is created
var framebuffer = new Framebuffer(); // Assume a valid Framebuffer is created
var dependencies = new Dependencies();

commandBuffer.BeginRendering(renderPass, framebuffer, dependencies);

// Record some commands
commandBuffer.BindViewport(new ViewportF(0, 0, 1920, 1080));
commandBuffer.BindScissorRect(new ScissorRect(0, 0, 1920, 1080));
commandBuffer.Draw(vertexCount: 3);

// End the rendering pass
commandBuffer.EndRendering();

// Validate recorded commands
foreach (var cmd in commandBuffer.RecordedCommands)
{
    Console.WriteLine(cmd);
}
```

### Submitting a Command Buffer

```csharp
// Submit the command buffer
var presentTexture = mockContext.CurrentSwapchainTexture;
var submitHandle = mockContext.Submit(commandBuffer, presentTexture, new KeyedMutexSyncInfo());

// Wait for submission to complete
mockContext.Wait(submitHandle);

Console.WriteLine("Command buffer submitted and completed.");
```

### Creating and Managing Mock Resources

```csharp
// Create a buffer
var bufferDesc = new BufferDesc
{
    DataSize = 1024,
    Usage = BufferUsageBits.VertexBuffer
};

mockContext.CreateBuffer(bufferDesc, out var buffer, "TestBuffer");

// Create a texture
var textureDesc = new TextureDesc
{
    Type = TextureType.Texture2D,
    Format = Format.RGBA_UN8,
    Dimensions = new Dimensions(512, 512, 1),
    Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment,
    NumMipLevels = 1,
    NumLayers = 1
};

mockContext.CreateTexture(textureDesc, out var texture, "TestTexture");
```

## Architecture Notes

The `HelixToolkit.Nex.Graphics.Mock` package is built on the following principles and dependencies:

- **Interface-based Design**: The mock implementations (`MockContext` and `MockCommandBuffer`) adhere to the `IContext` and `ICommandBuffer` interfaces, ensuring compatibility with the rest of the HelixToolkit.Nex engine.
- **Resource Simulation**: GPU resources such as buffers, textures, and pipelines are represented as mock objects, allowing for their creation, management, and validation without actual GPU interaction.
- **Command Recording**: The `MockCommandBuffer` class records all issued commands, enabling developers to verify the correctness of their rendering logic by inspecting the recorded command list.
- **Thread Safety**: The `MockContext` uses thread-safe collections to manage resources, ensuring safe concurrent access during testing.
- **Dependencies**: This package depends on the core HelixToolkit.Nex packages, including `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Maths`.

By providing a robust and flexible testing framework, `HelixToolkit.Nex.Graphics.Mock` empowers developers to build reliable and maintainable 3D graphics applications with confidence.
```
