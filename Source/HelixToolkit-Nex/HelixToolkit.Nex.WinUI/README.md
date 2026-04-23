```markdown
# HelixToolkit.Nex.WinUI

`HelixToolkit.Nex.WinUI` is a specialized package within the HelixToolkit.Nex ecosystem that provides a WinUI 3 control for rendering 3D graphics using the Vulkan-based HelixToolkit.Nex engine. It integrates seamlessly with the WinUI framework, enabling developers to create high-performance, interactive 3D applications with modern rendering techniques.

## Overview

`HelixToolkit.Nex.WinUI` serves as the bridge between the HelixToolkit.Nex 3D engine and the WinUI 3 framework. It provides a `HelixViewport` control that hosts the engine's rendering output within a `SwapChainPanel`. This package leverages advanced rendering features such as Vulkan-to-Direct3D11 interop, keyed mutex synchronization, and shared textures for efficient rendering in a WinUI environment.

Key features include:
- Hosting 3D content in WinUI 3 applications.
- Support for Vulkan-to-Direct3D11 interop via DXGI swap chains.
- Integration with the HelixToolkit.Nex engine for scene rendering and camera control.
- Event-driven architecture for custom rendering workflows.

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `HelixViewport`               | A WinUI 3 control for rendering 3D content using the HelixToolkit.Nex engine. |
| `HelixProperty`               | A utility class for registering dependency properties in WinUI.             |
| `FrameworkPropertyMetadata`   | Metadata for dependency properties, supporting advanced property behaviors. |
| `FrameworkPropertyMetadataOptions` | Enum defining options for dependency property behaviors, such as layout and rendering effects. |

## Usage Examples

### Creating a Custom Dependency Property

The `HelixProperty` class simplifies the creation of dependency properties for WinUI controls. Here's an example of registering a custom dependency property:

```csharp
using Microsoft.UI.Xaml;
using HelixToolkit.Nex.WinUI;

public class MyControl : DependencyObject
{
    public static readonly DependencyProperty MyProperty = HelixProperty.Register<MyControl, string>(
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

### Using the `HelixViewport` Control

The `HelixViewport` control is the primary way to integrate the HelixToolkit.Nex engine into a WinUI application. Below is an example of how to set up a `HelixViewport` in XAML and configure it in code-behind:

#### XAML
```xml
<Window
    x:Class="MyApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:helix="using:HelixToolkit.Nex.WinUI"
    Title="3D Viewer">
    <Grid>
        <helix:HelixViewport x:Name="Viewport" />
    </Grid>
</Window>
```

#### Code-Behind
```csharp
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.WinUI;
using Microsoft.UI.Xaml;

namespace MyApp
{
    public sealed partial class MainWindow : Window
    {
        private Engine _engine;
        private IViewportClient _viewportClient;
        private ICameraController _cameraController;

        public MainWindow()
        {
            this.InitializeComponent();

            // Initialize the HelixToolkit.Nex engine
            _engine = new Engine();
            _engine.Initialize();

            // Set up the viewport client and camera controller
            _viewportClient = new MyViewportClient();
            _cameraController = new OrbitCameraController();

            // Configure the HelixViewport
            Viewport.Engine = _engine;
            Viewport.ViewportClient = _viewportClient;
            Viewport.CameraController = _cameraController;

            // Subscribe to the BeforeRender event
            Viewport.BeforeRender += OnBeforeRender;
        }

        private void OnBeforeRender(object sender, ViewportRenderingEventArgs e)
        {
            // Perform custom operations before rendering
        }
    }
}
```

### Handling Pointer Events in `HelixViewport`

The `HelixViewport` control supports pointer events for user interaction. Here's an example of handling pointer input:

```csharp
Viewport.PointerPressed += (sender, args) =>
{
    var point = args.GetCurrentPoint(Viewport);
    System.Diagnostics.Debug.WriteLine($"Pointer pressed at {point.Position}");
};
```

## Architecture Notes

- **Design Patterns**: 
  - The `HelixViewport` control follows the MVVM pattern, allowing developers to bind a `ViewportClient` for scene management and a `CameraController` for camera interactions.
  - The `HelixProperty` class simplifies dependency property registration, adhering to the Dependency Property pattern in WinUI.

- **Dependencies**:
  - **HelixToolkit.Nex.Engine**: Provides the core 3D rendering engine, including the ECS, Render Graph, and Vulkan-based rendering pipeline.
  - **HelixToolkit.Nex.Graphics**: Supplies low-level graphics utilities and abstractions.
  - **Microsoft.UI.Xaml**: The WinUI 3 framework for building modern Windows applications.
  - **Vortice.DXGI** and **Vortice.Direct3D11**: Used for Direct3D11 and DXGI interop.
  - **Arch ECS**: The Entity Component System framework used by the HelixToolkit.Nex engine.

- **Vulkan-to-D3D11 Interop**:
  - The `HelixViewport` uses a DXGI swap chain for rendering and employs keyed mutex synchronization to manage access to shared textures between Vulkan and Direct3D11.

- **Event-Driven Rendering**:
  - The `BeforeRender` event allows developers to hook into the rendering pipeline and perform custom operations before each frame is rendered.

## Getting Started

To get started with `HelixToolkit.Nex.WinUI`, install the NuGet package:

```sh
dotnet add package HelixToolkit.Nex.WinUI
```

Then, follow the usage examples above to integrate the `HelixViewport` control into your WinUI application and start building high-performance 3D experiences.
```
