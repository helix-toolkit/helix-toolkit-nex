```markdown
# HelixToolkit.Nex.ImGui

HelixToolkit.Nex.ImGui is a package designed to integrate ImGui, a popular immediate-mode GUI library, into the HelixToolkit.Nex 3D graphics engine. It provides tools to render ImGui interfaces using Vulkan and manage ImGui configurations, enabling developers to create interactive debugging tools, overlays, and user interfaces within their applications.

## Overview

The HelixToolkit.Nex.ImGui package is responsible for rendering ImGui interfaces within the HelixToolkit.Nex engine. It leverages the Vulkan API for efficient rendering and integrates seamlessly with the engine's ECS-based architecture and Render Graph system. Key features include:
- Support for custom fonts and scaling via `ImGuiConfig`.
- GPU-based rendering of ImGui elements using shaders.
- Integration with the HelixToolkit.Nex rendering pipeline.
- Easy setup and initialization for ImGui contexts.

This package is ideal for developers looking to add interactive GUI elements to their 3D applications built with HelixToolkit.Nex.

## Key Types

| Type                | Description                                                                 |
|---------------------|-----------------------------------------------------------------------------|
| `ImGuiConfig`       | Configuration class for ImGui, including font path and size settings.       |
| `ImGuiRenderer`     | Main class for rendering ImGui interfaces. Handles initialization, font setup, and rendering. |

## Usage Examples

### Basic Setup

```csharp
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Graphics;

var context = /* Obtain your IContext instance */;
var config = new ImGuiConfig
{
    FontPath = "path/to/font.ttf",
    FontSizeInPixel = 18
};

var renderer = new ImGuiRenderer(context, config);

// Initialize the renderer with the target format
if (!renderer.Initialize(Format.RGBA_UN8))
{
    Console.WriteLine("Failed to initialize ImGuiRenderer.");
    return;
}

// Set the display scale for high-DPI screens
renderer.DisplayScale = 1.5f;

// Render ImGui in your rendering loop
renderer.SetFont("path/to/another/font.ttf"); // Optional: Change font dynamically
```

### Rendering ImGui Interfaces

```csharp
using ImGuiNET;

// Inside your rendering loop
Gui.NewFrame();

// Create a simple ImGui window
Gui.Begin("Example Window");
Gui.Text("Hello, ImGui!");
Gui.End();

Gui.Render();
renderer.Render(); // Render the ImGui frame
```

### Custom Font Configuration

```csharp
var config = new ImGuiConfig
{
    FontPath = "path/to/custom-font.ttf",
    FontSizeInPixel = 20
};

renderer.SetFont(config.FontPath);
```

## Architecture Notes

- **Design Patterns**: 
  - The package uses the **Dependency Injection** pattern to inject `IContext` into `ImGuiRenderer`.
  - The **Builder Pattern** is employed for constructing Vulkan pipeline descriptors.
  
- **Dependencies**:
  - Relies on `HelixToolkit.Nex.Graphics` for Vulkan context and resource management.
  - Uses `HelixToolkit.Nex.Shaders` for shader compilation and management.
  - Integrates with ImGui.NET, a .NET wrapper for ImGui.

- **Shader Design**:
  - The vertex and fragment shaders are tailored for ImGui rendering, supporting features like texture sampling and color blending.
  - Push constants are used for efficient data transfer to shaders.

HelixToolkit.Nex.ImGui simplifies the process of integrating ImGui into Vulkan-based applications, providing a robust and extensible solution for GUI rendering within the HelixToolkit.Nex ecosystem.
```
