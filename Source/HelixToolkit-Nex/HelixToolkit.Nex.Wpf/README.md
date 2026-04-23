```markdown
# HelixToolkit.Nex.Wpf

`HelixToolkit.Nex.Wpf` is a WPF integration package for the HelixToolkit.Nex 3D graphics engine. It enables seamless rendering of 3D content within WPF applications by leveraging Direct3D and Vulkan interoperability. The package provides a high-performance rendering pipeline, supporting advanced features such as GPU-based instance culling, forward-plus lighting, and screen-space mesh picking.

## Overview

`HelixToolkit.Nex.Wpf` bridges the gap between the HelixToolkit.Nex engine and WPF, allowing developers to embed 3D content into WPF applications with minimal effort. It uses `D3DImage` for efficient interop between Direct3D 9 and Vulkan, enabling real-time rendering in WPF applications. The package supports multiple viewports sharing a single rendering engine, making it suitable for complex applications with multiple 3D views.

### Key Features
- **Direct3D 9 and Vulkan Interop**: Uses `D3DImage` with shared textures for efficient rendering.
- **Multi-Viewport Support**: Share a single `Engine` instance across multiple `HelixViewport` controls.
- **Customizable Rendering**: Assign custom `ViewportClient` implementations to control camera and scene data.
- **Event Hooks**: Provides events like `BeforeRender` for read-only notifications during the rendering pipeline.

## Key Types

| Type                          | Description                                                                                      |
|-------------------------------|--------------------------------------------------------------------------------------------------|
| `D3D9DeviceManager`           | Manages the Direct3D 9 device and context for `D3DImage` interop.                               |
| `HelixProperty`               | Provides utility methods for registering WPF dependency properties and attached properties.     |
| `HelixViewport`               | A WPF `FrameworkElement` that hosts the HelixToolkit.Nex 3D engine output.                     |
| `ViewportRenderingEventArgs`  | Provides event data for the `BeforeRender` event in `HelixViewport`.                           |

## Usage Examples

### 1. Setting up a `HelixViewport` in XAML
```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:helix="clr-namespace:HelixToolkit.Nex.Wpf;assembly=HelixToolkit.Nex.Wpf"
        Title="3D Viewer" Height="450" Width="800">
    <Grid>
        <helix:HelixViewport x:Name="Viewport" />
    </Grid>
</Window>
```

### 2. Configuring the `HelixViewport` in Code-Behind
```csharp
using System.Windows;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Wpf;

namespace MyApp
{
    public partial class MainWindow : Window
    {
        private Engine _engine;
        private IViewportClient _viewportClient;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize the HelixToolkit.Nex engine
            _engine = new Engine();
            _engine.Initialize();

            // Assign the engine to the viewport
            Viewport.Engine = _engine;

            // Create and assign a custom viewport client
            _viewportClient = new MyViewportClient();
            Viewport.ViewportClient = _viewportClient;

            // Hook into the BeforeRender event
            Viewport.BeforeRender += OnBeforeRender;
        }

        private void OnBeforeRender(object sender, ViewportRenderingEventArgs e)
        {
            // Perform any read-only operations before rendering
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose of resources
            Viewport.Dispose();
            _engine.Dispose();
        }
    }
}
```

### 3. Registering a Dependency Property with `HelixProperty`
```csharp
using System.Windows;
using HelixToolkit.Nex.Wpf;

public class MyControl : FrameworkElement
{
    public static readonly DependencyProperty MyProperty =
        HelixProperty.Register<MyControl, string>(
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

## Architecture Notes

### Design Patterns
- **Dependency Injection**: The `HelixViewport` control relies on external injection of the `Engine` and `ViewportClient` to decouple rendering logic from the control itself.
- **Event-Driven Rendering**: The `BeforeRender` event allows developers to hook into the rendering pipeline for read-only notifications.
- **Interop Abstraction**: The `D3D9DeviceManager` abstracts the complexity of Direct3D 9 device creation and management for WPF interop.

### Dependencies
- **HelixToolkit.Nex.Engine**: Provides the core 3D graphics engine, including the ECS-based rendering pipeline and Vulkan integration.
- **Vortice.Direct3D9**: Used for Direct3D 9 device and context management.
- **Vortice.Vulkan**: Used for Vulkan API interop and rendering.
- **Arch ECS Library**: Powers the Entity Component System (ECS) architecture of the HelixToolkit.Nex engine.

### Interop Workflow
1. A `D3D9DeviceManager` creates a Direct3D 9 device and a shared back buffer.
2. The back buffer is shared with Direct3D 11 using a shared handle.
3. The shared texture is imported into Vulkan using the `VK_KHR_external_memory_win32` extension.
4. The Vulkan texture is used as the final output target for the `RenderContext`.
5. The `D3DImage` is updated with the shared back buffer to display the rendered content in WPF.

For more details, refer to the [HelixToolkit.Nex documentation](https://github.com/helix-toolkit/helix-toolkit).
```
