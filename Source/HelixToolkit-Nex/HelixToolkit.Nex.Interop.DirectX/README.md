```markdown
# HelixToolkit.Nex.Interop.DirectX

HelixToolkit.Nex.Interop.DirectX provides DirectX interop functionality for the HelixToolkit.Nex 3D graphics engine. It enables seamless integration between DirectX and Vulkan, supporting shared texture creation, external memory import, and viewport interaction. This package is essential for applications requiring efficient cross-API resource sharing, such as WPF and WinUI rendering pipelines.

## Overview

HelixToolkit.Nex.Interop.DirectX bridges DirectX and Vulkan APIs, enabling resource sharing and interop for advanced rendering scenarios. Key responsibilities include:
- Managing DirectX devices and contexts.
- Creating shared textures for Vulkan interop.
- Importing shared handles into Vulkan as external memory.
- Supporting viewport interaction and per-frame updates.

This package fits into the HelixToolkit.Nex engine by facilitating DirectX-Vulkan interop, allowing applications to leverage the strengths of both APIs.

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `D3D11DeviceManager`          | Manages the D3D11 device and provides the DXGI adapter LUID.               |
| `SharedTextureFactory`        | Creates shared D3D11 textures for Vulkan interop.                          |
| `SharedTextureResult`         | Represents the result of creating a shared texture, including dimensions.  |
| `SharedHandleType`            | Enum identifying the type of shared handle (KMT or NT).                   |
| `ImportedVulkanTexture`       | Represents a Vulkan texture imported from a shared DirectX handle.         |
| `VulkanExternalMemoryImporter`| Imports shared DirectX handles into Vulkan as external memory.             |
| `IViewportClient`             | Interface for per-frame viewport updates and data provision.               |
| `ViewportRenderingEventArgs`  | Event args for viewport rendering notifications.                           |
| `ViewportMouseButton`         | Enum for mouse button bindings in viewport camera interaction.             |

## Usage Examples

### Creating a Shared Texture for WPF
```csharp
using HelixToolkit.Nex.Interop.DirectX;

var d3d11Manager = new D3D11DeviceManager();
nint d3d9SharedHandle = ...; // Obtain from D3D9
var sharedTexture = SharedTextureFactory.CreateForWpf(d3d11Manager, d3d9SharedHandle);

Console.WriteLine($"Shared Handle: {sharedTexture.SharedHandle}");
Console.WriteLine($"Texture Dimensions: {sharedTexture.Width}x{sharedTexture.Height}");
```

### Creating a Shared Texture for WinUI
```csharp
using HelixToolkit.Nex.Interop.DirectX;

var d3d11Manager = new D3D11DeviceManager();
var sharedTexture = SharedTextureFactory.CreateForWinUI(d3d11Manager, 1920, 1080);

Console.WriteLine($"Shared Handle: {sharedTexture.SharedHandle}");
Console.WriteLine($"Texture Dimensions: {sharedTexture.Width}x{sharedTexture.Height}");
```

### Importing a Shared Handle into Vulkan
```csharp
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Vulkan;

var vulkanContext = ...; // Obtain VulkanContext
var sharedHandle = ...; // Obtain shared handle from D3D11
var importedTexture = VulkanExternalMemoryImporter.Import(
    vulkanContext,
    sharedHandle,
    VkExternalMemoryHandleTypeFlags.D3D11TextureBit,
    VkFormat.R8G8B8A8Unorm,
    1920,
    1080
);

Console.WriteLine($"Imported Vulkan Texture Handle: {importedTexture.Handle}");
```

### Implementing a Custom Viewport Client
```csharp
using HelixToolkit.Nex.Interop;

public class CustomViewportClient : IViewportClient
{
    public IRenderDataProvider? DataProvider { get; private set; }

    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        // Update camera or animations here
        return context.CameraParams;
    }
}
```

### Handling Viewport Rendering Events
```csharp
using HelixToolkit.Nex.Interop;

void OnBeforeRender(object sender, ViewportRenderingEventArgs e)
{
    Console.WriteLine($"Frame Time: {e.DeltaTime}s");
    // Optional diagnostics or debug overlays
}

viewport.BeforeRender += OnBeforeRender;
```

## Architecture Notes

- **DirectX-Vulkan Interop**: Shared textures are created using DirectX APIs and imported into Vulkan using VK_KHR_external_memory_win32. This enables efficient resource sharing between the two APIs.
- **Device Management**: `D3D11DeviceManager` centralizes DirectX device and context management, ensuring consistent access across the application.
- **ECS Integration**: Interop components integrate seamlessly with the ECS-based architecture of HelixToolkit.Nex, enabling efficient resource management and rendering.
- **Render Graph**: Shared textures and imported Vulkan resources are registered in the engine's `TexturesPool`, which is managed by the render graph for optimized resource usage.
- **Viewport Interaction**: The `IViewportClient` interface provides a modern approach to per-frame updates, replacing legacy event-based patterns for viewport rendering.

This package depends on HelixToolkit.Nex core packages for rendering, ECS, and Vulkan context management, as well as the Vortice.Direct3D and Vortice.Vulkan libraries for DirectX and Vulkan interop.
```
