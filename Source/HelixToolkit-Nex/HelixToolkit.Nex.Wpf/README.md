```markdown
# HelixToolkit.Nex.Wpf

HelixToolkit.Nex.Wpf is a specialized package within the HelixToolkit.Nex 3D graphics engine that provides WPF integration for rendering 3D content. It enables seamless interop between WPF's `D3DImage` and the Vulkan-based rendering capabilities of HelixToolkit.Nex, allowing developers to embed high-performance 3D graphics into WPF applications.

## Overview

HelixToolkit.Nex.Wpf bridges the gap between WPF's UI framework and the advanced rendering features of HelixToolkit.Nex. It leverages Direct3D 9 for `D3DImage` back buffer management and Vulkan for high-performance rendering. The package is designed to support scenarios such as real-time 3D visualization, CAD applications, and interactive simulations within WPF applications.

Key features include:
- **D3DImage Interop**: Uses Direct3D 9 to manage shared textures for rendering in WPF.
- **Vulkan Integration**: Imports shared textures into Vulkan for advanced rendering.
- **Viewport Control**: Provides the `HelixViewport` WPF control for embedding 3D content.
- **Dependency Property Utilities**: Simplifies the creation of WPF dependency properties.

## Key Types

| Type                  | Description                                                                 |
|-----------------------|-----------------------------------------------------------------------------|
| `D3D9DeviceManager`   | Manages the Direct3D 9 context and device for `D3DImage` interop.           |
| `HelixProperty`       | Utility class for registering WPF dependency properties and attached properties. |
| `HelixViewport`       | WPF control for hosting the HelixToolkit.Nex 3D engine output.             |

## Usage Examples

### Creating a `HelixViewport` Control
The `HelixViewport` control is the main entry point for rendering 3D content in WPF. Below is an example of how to use it in a WPF application:

```csharp
using System.Windows;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Wpf;

namespace Wpf3DExample
{
    public partial class MainWindow : Window
    {
        private Engine _engine;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize the HelixToolkit.Nex engine
            _engine = new Engine();

            // Create a HelixViewport control
            var viewport = new HelixViewport
            {
                Engine = _engine,
                ViewportClient = new MyViewportClient(),
                CameraController = new MyCameraController()
            };

            // Add the viewport to the WPF window
            Content = viewport;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _engine.Dispose();
        }
    }
}
```

### Registering Dependency Properties
The `HelixProperty` class simplifies the creation of WPF dependency properties. Here's an example:

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

### Managing D3D9 Context
The `D3D9DeviceManager` is used internally by `HelixViewport` but can also be used directly for advanced scenarios:

```csharp
using HelixToolkit.Nex.Wpf;

using (var deviceManager = new D3D9DeviceManager())
{
    var context = deviceManager.Context;
    var device = deviceManager.Device;

    // Use the context and device for custom rendering logic
}
```

## Architecture Notes

- **Direct3D 9 Interop**: The package uses Direct3D 9's `IDirect3DDevice9Ex` to create shared textures compatible with WPF's `D3DImage`.
- **Vulkan Integration**: Shared textures are imported into Vulkan using the `VK_KHR_external_memory_win32` extension for high-performance rendering.
- **Entity Component System (ECS)**: While the core HelixToolkit.Nex engine uses ECS for scene management, `HelixViewport` focuses on viewport-specific rendering.
- **Render Graph**: The HelixToolkit.Nex engine uses a Render Graph to manage rendering tasks, which is seamlessly integrated into `HelixViewport`.
- **Dependencies**: This package depends on other HelixToolkit.Nex components, such as the core engine and interop utilities, to provide its functionality.

HelixToolkit.Nex.Wpf is an essential component for developers looking to integrate high-performance 3D graphics into WPF applications while leveraging the power of the Vulkan API.
```
