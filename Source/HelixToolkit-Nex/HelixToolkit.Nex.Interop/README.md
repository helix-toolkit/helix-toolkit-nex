```markdown
# HelixToolkit.Nex.Interop

## Overview

`HelixToolkit.Nex.Interop` provides interoperability features for the HelixToolkit-Nex 3D graphics engine, particularly focusing on Vulkan and DirectX integration. This package includes utilities for importing textures and managing viewport interactions.

## Key Components

### IViewportClient

The `IViewportClient` interface provides per-frame data and updates for a `HelixViewport`. Implementations of this interface are set on the viewport's `ViewportClient` dependency property. The viewport calls `Update` each frame before rendering and reads `DataProvider` to obtain the scene data. If `DataProvider` returns `null`, the frame is skipped.

#### Properties

- `IRenderDataProvider? DataProvider`: Gets the render data provider for the current frame. Return `null` to skip the frame.

#### Methods

- `ICameraParamsProvider Update(RenderContext context, float deltaTime)`: Called once per frame before rendering. Use this to update the camera, tick animations, or perform any other per-frame work.

### ImportedVulkanTexture

The `ImportedVulkanTexture` class represents the result of importing a shared DirectX texture into Vulkan. It owns the `VkImage`, `VkDeviceMemory`, `VkImageView`, and `TextureHandle`.

#### Properties

- `TextureHandle Handle`: The texture handle registered in the engine's TexturesPool.
- `VkImage Image`: The imported `VkImage` backed by shared DirectX memory.
- `VkDeviceMemory Memory`: The `VkDeviceMemory` allocated via `ImportMemoryWin32HandleInfoKHR`.
- `VkImageView ImageView`: The `VkImageView` created for the imported image.

### ViewportMouseButton

The `ViewportMouseButton` enum identifies a mouse button for viewport camera interaction bindings. It is used to configure which button triggers rotate, pan, or zoom actions.

#### Enum Values

- `None`: No mouse button assigned; the action is disabled.
- `Left`: The left mouse button.
- `Middle`: The middle mouse button (wheel click).
- `Right`: The right mouse button.

### ViewportRenderingEventArgs

The `ViewportRenderingEventArgs` class provides read-only event arguments raised by `HelixViewport.BeforeRender` each frame. This event is a notification only — subscribers can use it for diagnostics, debug overlays, or other optional per-frame work.

#### Properties

- `RenderContext RenderContext`: The per-viewport render context (window size, camera, final output texture).
- `float DeltaTime`: Seconds elapsed since the previous frame.

### VulkanExternalMemoryImporter

The `VulkanExternalMemoryImporter` class provides functionality to import a shared DirectX texture handle into Vulkan as a `VkImage` using `VK_KHR_external_memory_win32`.

#### Methods

- `static ImportedVulkanTexture Import(IContext context, nint sharedHandle, VkExternalMemoryHandleTypeFlags handleType, VkFormat format, uint width, uint height)`: Imports a shared handle into Vulkan using `VK_KHR_external_memory_win32`. Creates a `VkImage` with `ExternalMemoryImageCreateInfo`, allocates memory with `ImportMemoryWin32HandleInfoKHR`, and wraps it as a `TextureHandle`.

## Usage Example

```csharp
public class MyViewportClient : IViewportClient
{
    public IRenderDataProvider? DataProvider { get; private set; }

    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        // Update camera or animations here
        return new MyCameraParamsProvider();
    }
}

// Usage in a HelixViewport
var viewport = new HelixViewport();
viewport.ViewportClient = new MyViewportClient();
```

This example demonstrates how to implement the `IViewportClient` interface to provide custom per-frame updates and data to a `HelixViewport`.
```
