```markdown
# HelixToolkit.Nex.ImGui

The `HelixToolkit.Nex.ImGui` package provides integration with the ImGui library, enabling developers to create rich, immediate-mode graphical user interfaces within the HelixToolkit.Nex 3D graphics engine. This package facilitates the rendering of ImGui elements using Vulkan, leveraging the HelixToolkit.Nex's rendering capabilities.

## Overview

`HelixToolkit.Nex.ImGui` serves as a bridge between the ImGui library and the HelixToolkit.Nex engine, allowing developers to render ImGui interfaces within their 3D applications. It manages the setup and rendering of ImGui contexts, including font management and shader compilation. The package is designed to work seamlessly with the HelixToolkit.Nex's rendering pipeline, utilizing its ECS architecture and render graph system.

## Key Types

| Type            | Description                                                                 |
|-----------------|-----------------------------------------------------------------------------|
| `ImGuiConfig`   | Configuration class for setting up ImGui, including font path and size.     |
| `ImGuiRenderer` | Main class responsible for rendering ImGui elements within the engine.      |

## Usage Examples

### Basic Setup

To use the `ImGuiRenderer`, you need to initialize it with a rendering context and configuration, then integrate it into your rendering loop.

```csharp
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Graphics;

// Create an ImGui configuration
var config = new ImGuiConfig
{
    FontPath = "path/to/font.ttf",
    FontSizeInPixel = 18
};

// Initialize the ImGui renderer with a rendering context
var context = /* Obtain IContext from HelixToolkit.Nex */;
var imguiRenderer = new ImGuiRenderer(context, config);

// Initialize the renderer with the target format
imguiRenderer.Initialize(Format.RGBA_UN8);

// In your rendering loop
imguiRenderer.BeginFrame(new Vector2(windowWidth, windowHeight));

// ImGui commands go here
ImGuiNET.ImGui.Text("Hello, ImGui!");

imguiRenderer.EndFrame();

// Render ImGui
imguiRenderer.Render(commandBuffer, renderPass, framebuffer, dependencies);
```

## Architecture Notes

- **Design Patterns**: The `ImGuiRenderer` utilizes the IDisposable pattern to manage resources effectively, ensuring that Vulkan resources are properly released.
- **Dependencies**: This package depends on the `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Shaders` packages for rendering and shader management.
- **Integration**: The renderer is designed to fit into the HelixToolkit.Nex's ECS and render graph architecture, allowing for efficient and organized rendering of ImGui elements alongside other 3D content.
```
