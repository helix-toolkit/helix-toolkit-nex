```markdown
# HelixToolkit.Nex.Wpf

HelixToolkit.Nex.Wpf is a package designed to integrate the HelixToolkit.Nex 3D graphics engine with Windows Presentation Foundation (WPF) applications. It provides a seamless way to render 3D content within WPF applications using the Vulkan API, leveraging Direct3D9 for interop with WPF's D3DImage.

## Overview

HelixToolkit.Nex.Wpf bridges the gap between the HelixToolkit.Nex engine and WPF, allowing developers to embed high-performance 3D graphics into their WPF applications. The package utilizes Direct3D9 for creating shared textures that can be used as back buffers in WPF, and Vulkan for rendering. This setup enables efficient rendering and resource sharing between the graphics engine and the WPF UI.

Key concepts include:
- **D3D9 and Vulkan Interop**: Uses Direct3D9 to create shared textures for WPF's D3DImage, which are then imported into Vulkan for rendering.
- **Viewport Management**: Provides a `HelixViewport` control for hosting 3D content, supporting multiple viewports sharing a single engine instance.
- **Dependency Properties**: Simplifies the creation and management of dependency properties within WPF.

## Key Types

| Type                     | Description                                                                 |
|--------------------------|-----------------------------------------------------------------------------|
| `D3D9DeviceManager`      | Manages the D3D9 context and device for WPF D3DImage interop.               |
| `HelixProperty`          | Provides static methods to register dependency properties and attached properties. |
| `HelixViewport`          | A WPF control that hosts the HelixToolkit.Nex 3D engine output.             |

## Usage Examples

### Creating a HelixViewport

```csharp
var viewport = new HelixViewport();
viewport.BeforeRender += (sender, args) =>
{
    // Handle pre-render logic here
};
```

### Registering a Dependency Property

```csharp
public static readonly DependencyProperty MyProperty = HelixProperty.Register<MyControl, int>(
    "MyProperty",
    0,
    isTwoWayBinding: true
);
```

### Managing D3D9 Device

```csharp
using (var deviceManager = new D3D9DeviceManager())
{
    var context = deviceManager.Context;
    var device = deviceManager.Device;
    // Use context and device for rendering operations
}
```

## Architecture Notes

- **Design Patterns**: The package employs the Factory pattern for creating shared textures and importing them into Vulkan. It also uses the Observer pattern for event handling in the `HelixViewport`.
- **Dependencies**: Relies on the Vortice.Direct3D9 library for Direct3D9 operations and the HelixToolkit.Nex engine for Vulkan rendering.
- **Interop Strategy**: Utilizes VK_KHR_external_memory_win32 for sharing textures between Direct3D9 and Vulkan, ensuring efficient resource management and rendering performance.

## Recent Changes

- **HelixViewport Rendering Update**: The `Render` method now requires the imported texture handle as a parameter to ensure the correct texture is used during rendering. This change improves the clarity and correctness of the rendering process by explicitly passing the texture handle.
```
