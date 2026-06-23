```markdown
# HelixToolkit.Nex.Graphics.Mock

The `HelixToolkit.Nex.Graphics.Mock` package provides mock implementations of graphics interfaces for unit testing within the HelixToolkit.Nex 3D graphics engine. It allows developers to simulate graphics operations without requiring actual GPU access, facilitating the testing of rendering logic and command buffer management in a controlled environment.

## Overview

The `HelixToolkit.Nex.Graphics.Mock` package is designed to fit seamlessly into the HelixToolkit.Nex engine by providing mock implementations of key graphics interfaces. This package is primarily used for testing purposes, allowing developers to validate rendering logic, command buffer operations, and resource management without the need for actual hardware. The mock implementations track method calls, validate parameters, and return mock resources, making it easier to test and debug graphics-related code.

## Key Types

- **MockCommandBuffer**: A mock implementation of `ICommandBuffer` for unit testing, allowing the recording and validation of command buffer operations.
  - **DrawCallCount**: Property added to track the number of draw calls made.
  - **DispatchCallCount**: Property added to track the number of dispatch calls made.
  - **SetCheckpointMarker**: Method to simulate setting a checkpoint marker.
  - **Barrier**: Overloaded methods to simulate a barrier operation on one or multiple buffers, with additional overloads for `BarrierPreset` and `BarrierDescriptor`.
  - **ImageBarrier**: Methods to simulate image barrier operations with `ImageTransition` and `BarrierDescriptor`.
  - **BindRenderPipeline**: Overloaded method to bind a render pipeline with color write states.
  - **ClearDepthStencilImage**: Method to simulate clearing a depth-stencil image.
  - **SetColorWriteEnabled**: Overloaded methods to enable or disable color writes for attachments.
  - **CopyTextureToBuffer**: Method to simulate copying texture data to a buffer.
  - **SetCullMode**: Method to simulate setting the cull mode.
- **MockContext**: A mock implementation of `IContext` that simulates a graphics context, providing methods to create and manage mock resources such as buffers, textures, and pipelines.
  - **GenerateMipmap**: Method to simulate mipmap generation for a texture.
  - **WaitAll**: Method to simulate waiting for all operations to complete.
  - **IsReady**: Method to check if a submit handle is ready.
  - **Wait**: Overloaded method to simulate waiting on a submit handle with an optional reset parameter.
  - **CreateSecondaryCommandBuffer**: Method signature updated to remove `RenderPass` parameter.
  - **SupportsSubpass**: Property indicating if subpass operations are supported.
  - **GetBufferDesc**: Method to retrieve the `BufferDesc` used to create a buffer.
  - **GetBufferSubData**: Method to retrieve sub-data from a buffer.
  - **MarkDirty**: Method to mark a buffer as dirty, though no-op in mock context.

## Usage Examples

### Creating and Using a Mock Command Buffer

```csharp
var context = new MockContext();
context.Initialize();

var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.BeginRendering(new RenderPass(), new Framebuffer(), new Dependencies());
commandBuffer.BindViewport(new ViewportF(0, 0, 1920, 1080));
commandBuffer.Draw(3);
commandBuffer.EndRendering();

foreach (var command in commandBuffer.RecordedCommands)
{
    Console.WriteLine(command);
}
```

### Creating Mock Resources

```csharp
var context = new MockContext();
context.Initialize();

BufferResource buffer;
context.CreateBuffer(new BufferDesc { DataSize = 1024 }, out buffer, "TestBuffer");

TextureResource texture;
context.CreateTexture(new TextureDesc { Dimensions = new Dimensions(256, 256, 1) }, out texture, "TestTexture");
```

### Using New Methods

#### Barrier Operation

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.Barrier(buffer.Handle, force: true);
commandBuffer.Barrier(new[] { buffer.Handle }.AsSpan(), force: true);
commandBuffer.Barrier(buffer.Handle, BarrierPreset.FullBarrier, force: true);
commandBuffer.Barrier(new[] { buffer.Handle }.AsSpan(), BarrierPreset.FullBarrier, force: true);
```

#### Image Barrier Operation

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.ImageBarrier(texture.Handle, ImageTransition.TransferToShaderRead);
commandBuffer.ImageBarrier(texture.Handle, new BarrierDescriptor(), TextureLayout.ShaderReadOnly);
```

#### Mipmap Generation

```csharp
uint levels;
context.GenerateMipmap(texture.Handle, out levels);
```

#### Set Checkpoint Marker

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.SetCheckpointMarker("Checkpoint1".AsSpan());
```

#### Bind Render Pipeline with Color Writes

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.BindRenderPipeline(renderPipelineHandle, new[] { true, false, true }.AsSpan());
```

#### Clear Depth Stencil Image

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.ClearDepthStencilImage(texture.Handle, depth: 1.0f, stencil: 0);
```

#### Set Color Write Enabled

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.SetColorWriteEnabled(new[] { true, false, true }.AsSpan());
commandBuffer.SetColorWriteEnabled(c0: true, c1: false, c2: true, c3: false);
```

#### Copy Texture to Buffer

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.CopyTextureToBuffer(texture.Handle, buffer.Handle, 0, new Offset3D(0, 0, 0), new Dimensions(256, 256, 1), new TextureLayers(0, 1));
```

#### Set Cull Mode

```csharp
var commandBuffer = context.AcquireCommandBuffer();
commandBuffer.SetCullMode(CullMode.Back);
```

## Architecture Notes

- **Design Patterns**: The package uses mock objects to simulate the behavior of graphics interfaces, allowing for isolated testing of rendering logic.
- **Dependencies**: This package depends on other HelixToolkit.Nex packages, such as `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Maths`, to define the interfaces and data structures used in the mock implementations.
- **Integration**: The mock implementations are designed to integrate with the HelixToolkit.Nex engine's ECS and render graph systems, providing a seamless testing environment for developers.

## Recent Changes

- **Project Configuration**: Added new build configurations for `LinuxDebug` and `LinuxRelease` to support cross-platform testing and development.
- **New Methods and Properties**: Added `DrawCallCount`, `DispatchCallCount`, `SetCheckpointMarker`, `BindRenderPipeline` with color writes, `ClearDepthStencilImage`, `SetColorWriteEnabled`, `CopyTextureToBuffer`, `SetCullMode`, and updated `CreateSecondaryCommandBuffer` method signature to enhance testing capabilities.
- **SupportsSubpass**: Added `SupportsSubpass` property to `MockContext` to indicate subpass support.
- **GetBufferDesc**: Added method to `MockContext` to retrieve the `BufferDesc` used to create a buffer.
- **GetBufferSubData**: Added method to `MockContext` to retrieve sub-data from a buffer.
- **MarkDirty**: Added method to `MockContext` to mark a buffer as dirty, though it is a no-op in the mock context.
- **Barrier Overloads**: Added overloads to `MockCommandBuffer` for barrier operations on multiple buffers, with `BarrierPreset` and `BarrierDescriptor`.
- **ImageBarrier Methods**: Added methods to `MockCommandBuffer` for image barrier operations with `ImageTransition` and `BarrierDescriptor`.
```
