```markdown
# HelixToolkit.Nex.Interop.DirectX

The `HelixToolkit.Nex.Interop.DirectX` package provides interoperability between DirectX and Vulkan within the HelixToolkit-Nex 3D graphics engine. It facilitates the sharing of textures and resources between DirectX and Vulkan, enabling seamless integration and rendering across different graphics APIs.

## Overview

This package is designed to bridge the gap between DirectX and Vulkan by managing shared resources and device contexts. It plays a crucial role in scenarios where applications need to leverage both DirectX and Vulkan capabilities, such as in mixed rendering pipelines or when transitioning from DirectX to Vulkan. The package includes classes and interfaces for managing DirectX devices, creating shared textures, and importing these textures into Vulkan.

## Key Types

| Type Name                | Description                                                                 |
|--------------------------|-----------------------------------------------------------------------------|
| `D3D11DeviceManager`     | Manages the D3D11 device and provides the DXGI adapter LUID.                |
| `IViewportClient`        | Interface for providing per-frame data and updates for a `HelixViewport`.   |
| `ImportedVulkanTexture`  | Represents a Vulkan texture imported from a shared DirectX texture.         |
| `SharedHandleType`       | Enum identifying the type of shared handle used for DirectX-Vulkan interop. |
| `SharedTextureFactory`   | Static class for creating D3D11 textures in shared mode for Vulkan interop. |
| `SharedTextureResult`    | Result of creating a shared D3D11 texture for Vulkan interop.               |
| `ViewportMouseButton`    | Enum identifying a mouse button for viewport camera interaction.            |
| `ViewportRenderingEventArgs` | Event args for `HelixViewport.BeforeRender` notifications.              |
| `VulkanExternalMemoryImporter` | Imports a shared DirectX texture handle into Vulkan as a VkImage.    |

## Usage Examples

### Creating a Shared Texture for WPF

```csharp
var d3d11Manager = new D3D11DeviceManager();
nint d3d9SharedHandle = /* obtain from D3D9 */;
var sharedTexture = SharedTextureFactory.CreateForWpf(d3d11Manager, d3d9SharedHandle);
```

### Importing a Shared Texture into Vulkan

```csharp
var vulkanContext = /* obtain VulkanContext */;
nint sharedHandle = /* obtain shared handle */;
var importedTexture = VulkanExternalMemoryImporter.Import(
    vulkanContext,
    sharedHandle,
    VkExternalMemoryHandleTypeFlags.D3D11TextureKmtBit,
    VkFormat.B8G8R8A8Unorm,
    sharedTexture.Width,
    sharedTexture.Height
);
```

### Implementing IViewportClient

```csharp
public class MyViewportClient : IViewportClient
{
    public IRenderDataProvider? DataProvider { get; private set; }

    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        // Update camera or animations
        return /* return updated camera parameters */;
    }
}
```

## Architecture Notes

- **Design Patterns**: The package utilizes the Factory pattern for creating shared textures (`SharedTextureFactory`) and the Adapter pattern for managing device contexts (`D3D11DeviceManager`).
- **Dependencies**: This package depends on other HelixToolkit.Nex packages, specifically those related to Vulkan graphics (`HelixToolkit.Nex.Graphics.Vulkan`) and rendering contexts.
- **Interop Strategy**: The package employs VK_KHR_external_memory_win32 for Vulkan-DirectX interop, allowing textures to be shared across APIs efficiently.
```
