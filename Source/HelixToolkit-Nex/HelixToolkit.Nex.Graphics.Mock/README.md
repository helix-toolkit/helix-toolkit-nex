```markdown
# HelixToolkit.Nex.Graphics.Mock

The `HelixToolkit.Nex.Graphics.Mock` package provides mock implementations of graphics interfaces for unit testing within the HelixToolkit.Nex 3D graphics engine. It allows developers to simulate graphics operations without requiring actual GPU access, facilitating the testing of rendering logic and command buffer management in a controlled environment.

## Overview

The `HelixToolkit.Nex.Graphics.Mock` package is designed to fit seamlessly into the HelixToolkit.Nex engine by providing mock implementations of key graphics interfaces. This package is primarily used for testing purposes, allowing developers to validate rendering logic, command buffer operations, and resource management without the need for actual hardware. The mock implementations track method calls, validate parameters, and return mock resources, making it easier to test and debug graphics-related code.

## Key Types

- **MockCommandBuffer**: A mock implementation of `ICommandBuffer` for unit testing, allowing the recording and validation of command buffer operations.
  - **Barrier**: New method added to simulate a barrier operation on a buffer.
- **MockContext**: A mock implementation of `IContext` that simulates a graphics context, providing methods to create and manage mock resources such as buffers, textures, and pipelines.
  - **GenerateMipmap**: New method added to simulate mipmap generation for a texture.
  - **WaitAll**: New method added to simulate waiting for all operations to complete.
  - **IsReady**: New method added to check if a submit handle is ready.
  - **Wait**: Overloaded method added to simulate waiting on a submit handle with an optional reset parameter.

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
commandBuffer.Barrier(buffer.Handle);
```

#### Mipmap Generation

```csharp
uint levels;
context.GenerateMipmap(texture.Handle, out levels);
```

## Architecture Notes

- **Design Patterns**: The package uses mock objects to simulate the behavior of graphics interfaces, allowing for isolated testing of rendering logic.
- **Dependencies**: This package depends on other HelixToolkit.Nex packages, such as `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Maths`, to define the interfaces and data structures used in the mock implementations.
- **Integration**: The mock implementations are designed to integrate with the HelixToolkit.Nex engine's ECS and render graph systems, providing a seamless testing environment for developers.

## Recent Changes

- **Project Configuration**: Added new build configurations for `LinuxDebug` and `LinuxRelease` to support cross-platform testing and development.
- **New Methods**: Added `Barrier`, `GenerateMipmap`, `WaitAll`, `IsReady`, and an overloaded `Wait` method to enhance testing capabilities.
```
