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
| `ViewportLifecycle<TSession>` | Manages the lifecycle of a viewport session, handling loading, unloading, and disposing of resources. |

## Usage Examples

### Creating a HelixViewport

```csharp
var viewport = new HelixViewport();
// Handle viewport events and rendering logic
```

### Viewport Lifetime

`Unloaded` only suspends rendering so the same `HelixViewport` can be loaded
again. The owner must call `Dispose()` when the viewport instance will no
longer be used. Dispose every viewport before disposing its externally owned
`Engine`.

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

- **Platform Support Update**: The project now conditionally targets `net9.0-windows` for Windows platforms and `net9.0` for Linux platforms, reflecting improved cross-platform compatibility.
- **HelixViewport Rendering Update**: The `Render` method now requires the imported texture handle as a parameter to ensure the correct texture is used during rendering. This change improves the clarity and correctness of the rendering process by explicitly passing the texture handle.
- **Configuration Management**: Added explicit configuration management with `Debug` and `Release` configurations to streamline build processes.
- **Gamma Correction**: Enabled gamma correction in the `HelixViewport` by default to improve color accuracy in rendered scenes.
- **Resource Management**: Added `EnsureSize` method in `HelixViewport` to handle dynamic resizing of resources, ensuring the viewport adapts to size changes efficiently.
- **Event Handling Update**: Removed the `BeforeRender` event from `HelixViewport` to streamline the rendering process and reduce complexity.
- **D3DImage Management**: Improved D3DImage locking and unlocking logic to ensure thread safety and rendering correctness.
- **Project Reference Update**: Added a project reference to `HelixToolkit.Nex.Interop` to facilitate shared resource management across different platform targets.
- **Viewport Lifecycle Management**: Introduced `ViewportLifecycle<TSession>` to manage the lifecycle of viewport sessions, handling loading, unloading, and disposing of resources efficiently.
```
