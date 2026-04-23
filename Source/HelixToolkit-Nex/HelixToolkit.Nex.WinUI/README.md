```markdown
# HelixToolkit.Nex.WinUI

HelixToolkit.Nex.WinUI is a C# package designed to integrate the HelixToolkit.Nex 3D graphics engine with WinUI 3 applications. It provides a high-performance rendering control (`HelixViewport`) that leverages Vulkan and DirectX interop for seamless integration with the WinUI composition system. This package enables developers to create interactive 3D applications with advanced rendering capabilities, including support for shared engines, camera controllers, and scene management.

---

## Overview

HelixToolkit.Nex.WinUI bridges the gap between the HelixToolkit.Nex engine and the WinUI 3 framework. It provides a `HelixViewport` control that hosts the engine's rendering output within a WinUI application. The viewport uses a `SwapChainPanel` for rendering and employs Vulkan-to-D3D11 interop for efficient composition. Key features include:

- **Shared Engine Support**: Multiple viewports can share a single engine instance for optimized resource usage.
- **Customizable Viewport Clients**: Developers can assign custom `IViewportClient` implementations to control camera and scene updates.
- **Event-Driven Rendering**: The `BeforeRender` event allows developers to hook into the rendering pipeline for read-only notifications.
- **Keyed Mutex Synchronization**: Ensures smooth interop between Vulkan and DirectX rendering pipelines.

---

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `HelixViewport`               | A WinUI control that hosts the HelixToolkit.Nex engine's rendering output. |
| `FrameworkPropertyMetadata`   | Provides metadata for dependency properties, with options for layout and rendering behavior. |
| `FrameworkPropertyMetadataOptions` | Enum defining metadata flags for dependency properties, such as `AffectsMeasure` and `BindsTwoWayByDefault`. |
| `HelixProperty`               | Static class for registering dependency properties and attached properties. |

---

## Usage Examples

### Creating a Custom Dependency Property

The `HelixProperty` class simplifies the registration of dependency properties in WinUI. Below is an example of registering a custom dependency property with two-way binding:

```csharp
using Microsoft.UI.Xaml;
using HelixToolkit.Nex.WinUI;

public class CustomControl : DependencyObject
{
    public static readonly DependencyProperty MyProperty = HelixProperty.Register<CustomControl, string>(
        "MyProperty",
        defaultValue: "Default Value",
        isTwoWayBinding: true
    );

    public string MyProperty
    {
        get => (string)GetValue(MyProperty);
        set => SetValue(MyProperty, value);
    }
}
```

### Using the HelixViewport Control

The `HelixViewport` control can be added to a WinUI 3 application to display 3D content. Below is an example of setting up the viewport and assigning an engine:

```csharp
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.WinUI;
using Microsoft.UI.Xaml.Controls;

public sealed partial class MainPage : Page
{
    private Engine _engine;

    public MainPage()
    {
        this.InitializeComponent();

        // Initialize the HelixToolkit.Nex engine
        _engine = new Engine();
        _engine.Initialize();

        // Create and configure the HelixViewport
        var viewport = new HelixViewport
        {
            Width = 800,
            Height = 600,
        };

        // Assign the engine to the viewport
        viewport.Engine = _engine;

        // Add the viewport to the page
        Content = viewport;
    }
}
```

---

## Architecture Notes

### Design Patterns

- **Entity Component System (ECS)**: The HelixToolkit.Nex engine uses the Arch ECS library for efficient entity management, enabling scalable and modular scene composition.
- **Render Graph**: A render graph is employed to manage the execution order of rendering nodes, ensuring optimal performance and flexibility.
- **Forward Plus Lighting**: The engine uses Forward Plus light culling for efficient rendering of scenes with numerous light sources.
- **Reverse-Z Projection**: Reverse-Z is used for depth precision improvements in projection matrices.

### Dependencies

HelixToolkit.Nex.WinUI depends on the following HelixToolkit.Nex packages:
- **HelixToolkit.Nex.Engine**: Provides the core 3D graphics engine functionality.
- **HelixToolkit.Nex.Graphics**: Handles Vulkan-based rendering and GPU resource management.
- **HelixToolkit.Nex.Interop**: Facilitates interop between Vulkan and DirectX for shared texture rendering.

---

For more information, visit the [HelixToolkit.Nex documentation](https://github.com/helix-toolkit-nex).
```
