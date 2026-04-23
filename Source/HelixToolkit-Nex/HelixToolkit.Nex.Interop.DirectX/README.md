```markdown
# HelixToolkit.Nex.Interop.DirectX

The `HelixToolkit.Nex.Interop.DirectX` package provides interoperability between DirectX and Vulkan within the HelixToolkit.Nex 3D graphics engine. It enables seamless sharing of textures and resources between DirectX 11 and Vulkan, supporting scenarios like WPF and WinUI rendering pipelines. This package is essential for applications that require efficient GPU resource sharing across APIs.

## Overview

The `HelixToolkit.Nex.Interop.DirectX` package is designed to bridge the gap between DirectX and Vulkan, enabling resource sharing and synchronization. It provides utilities for creating and managing shared textures, importing DirectX resources into Vulkan, and managing DirectX devices. Key features include:

- **DirectX-Vulkan Interoperability**: Support for shared textures using KMT and NT handles.
- **Device Management**: Simplified management of DirectX 11 devices and contexts.
- **Shared Texture Creation**: Utilities for creating shared textures for WPF and WinUI pipelines.
- **Vulkan Resource Import**: Import DirectX textures into Vulkan using external memory extensions.

This package integrates seamlessly with the HelixToolkit.Nex engine, leveraging its ECS-based architecture and Vulkan rendering backend.

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `D3D11DeviceManager`          | Manages the D3D11 device and provides the DXGI adapter LUID.                |
| `IViewportClient`             | Interface for providing per-frame data and updates for a `HelixViewport`.  |
| `ImportedVulkanTexture`       | Represents a Vulkan texture imported from a shared DirectX handle.          |
| `SharedHandleType`            | Enum identifying the type of shared handle used for DirectX-Vulkan interop. |
| `SharedTextureFactory`        | Static class for creating shared D3D11 textures for Vulkan interop.         |
| `SharedTextureResult`         | Represents the result of creating a shared D3D11 texture.                  |
| `ViewportMouseButton`         | Enum identifying mouse buttons for viewport camera interaction.            |
| `ViewportRenderingEventArgs`  | Event arguments for per-frame rendering notifications.                     |
| `VulkanExternalMemoryImporter`| Static class for importing shared DirectX textures into Vulkan.            |

## Usage Examples

### Creating a Shared Texture for WPF
```csharp
var d3d11Manager = new D3D11DeviceManager();
nint d3d9SharedHandle = /* Obtain from D3D9 */;
var sharedTexture = SharedTextureFactory.CreateForWpf(d3d11Manager, d3d9SharedHandle);

Console.WriteLine($"Shared Handle: {sharedTexture.SharedHandle}");
Console.WriteLine($"Texture Dimensions: {sharedTexture.Width}x{sharedTexture.Height}");
```

### Creating a Shared Texture for WinUI
```csharp
var d3d11Manager = new D3D11DeviceManager();
uint width = 1920, height = 1080;
var sharedTexture = SharedTextureFactory.CreateForWinUI(d3d11Manager, width, height);

Console.WriteLine($"Shared Handle: {sharedTexture.SharedHandle}");
Console.WriteLine($"Texture Dimensions: {sharedTexture.Width}x{sharedTexture.Height}");
```

### Importing a Shared Texture into Vulkan
```csharp
var vulkanContext = /* Obtain VulkanContext */;
var sharedHandle = /* Obtain shared handle from DirectX */;
var importedTexture = VulkanExternalMemoryImporter.Import(
    vulkanContext,
    sharedHandle,
    VkExternalMemoryHandleTypeFlags.D3D11TextureBit,
    VkFormat.R8G8B8A8Unorm,
    1920,
    1080
);

Console.WriteLine($"Imported Texture Handle: {importedTexture.Handle}");
```

### Implementing an IViewportClient
```csharp
public class CustomViewportClient : IViewportClient
{
    public IRenderDataProvider? DataProvider { get; private set; }

    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        // Update camera or animations here
        return context.CameraParams;
    }
}

// Usage
var viewport = new HelixViewport();
viewport.ViewportClient = new CustomViewportClient();
```

## Architecture Notes

- **DirectX-Vulkan Interop**: The package relies on Vulkan's `VK_KHR_external_memory_win32` extension to import DirectX textures. Shared textures are created with either KMT or NT handles, depending on the rendering pipeline (WPF or WinUI).
- **Device Management**: The `D3D11DeviceManager` simplifies DirectX 11 device and context management, providing a unified interface for both WPF and WinUI applications.
- **Shared Texture Factory**: The `SharedTextureFactory` provides static methods for creating shared textures tailored to specific use cases, such as WPF (KMT handles) and WinUI (NT handles).
- **ECS Integration**: The `ImportedVulkanTexture` class integrates with the HelixToolkit.Nex ECS-based architecture by registering imported textures in the engine's `TexturesPool`.
- **Dependencies**: This package depends on the `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.Vulkan` libraries for DirectX and Vulkan interop functionality.

For more information, refer to the [HelixToolkit.Nex documentation](https://github.com/helix-toolkit/helix-toolkit).
```
