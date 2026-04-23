```markdown
# HelixToolkit.Nex.WinUI

HelixToolkit.Nex.WinUI is a C# package designed to integrate the HelixToolkit.Nex 3D graphics engine with WinUI 3 applications. It provides a set of controls and utilities to render 3D content using the Vulkan API, leveraging DirectX interop for efficient rendering within WinUI applications.

## Overview

HelixToolkit.Nex.WinUI serves as the bridge between the HelixToolkit.Nex engine and WinUI 3, enabling developers to create rich 3D experiences in modern Windows applications. It utilizes a `SwapChainPanel` for rendering, allowing seamless integration of 3D content with other UI elements. The package supports advanced rendering techniques such as keyed mutex synchronization for Vulkan-to-D3D11 interop, ensuring smooth and efficient rendering.

Key concepts include:
- **SwapChainPanel**: Utilized for rendering 3D content within WinUI.
- **Keyed Mutex Synchronization**: Ensures efficient resource sharing between Vulkan and DirectX.
- **Viewport Client**: Provides camera and scene data for rendering.

## Key Types

| Type                              | Description                                                                 |
|-----------------------------------|-----------------------------------------------------------------------------|
| `HelixViewport`                   | A WinUI 3 control that hosts the HelixToolkit.Nex 3D engine output.         |
| `FrameworkPropertyMetadata`       | Defines metadata for a dependency property, including default values.       |
| `FrameworkPropertyMetadataOptions`| Enum specifying options for dependency property behavior in the property system. |
| `HelixProperty`                   | Provides methods to register dependency properties and attached properties. |

## Usage Examples

### Creating a HelixViewport

```csharp
var viewport = new HelixViewport
{
    Engine = myEngineInstance, // Set the engine instance
    ViewportClient = myViewportClient // Set the viewport client for camera and scene data
};
myWinUIPage.Content = viewport;
```

### Registering a Dependency Property

```csharp
public static readonly DependencyProperty MyProperty = HelixProperty.Register<MyControl, int>(
    "MyProperty",
    0, // Default value
    true // Enable two-way binding
);
```

### Handling Pointer Events

```csharp
viewport.PointerPressed += (sender, e) =>
{
    // Handle pointer pressed event
};
```

## Architecture Notes

- **Design Patterns**: The package uses a component-based architecture, leveraging the Entity Component System (ECS) pattern for managing 3D entities and their behaviors.
- **Dependencies**: HelixToolkit.Nex.WinUI depends on the core HelixToolkit.Nex engine for rendering capabilities and the Arch ECS library for entity management.
- **Interop**: Utilizes DirectX interop for rendering within WinUI, ensuring compatibility and performance by using keyed mutex synchronization for resource sharing between Vulkan and DirectX.

HelixToolkit.Nex.WinUI is designed to be flexible and powerful, providing developers with the tools needed to create immersive 3D applications in the WinUI ecosystem.
```
