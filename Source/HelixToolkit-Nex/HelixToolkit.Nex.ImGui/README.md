```markdown
# HelixToolkit.Nex.ImGui

`HelixToolkit.Nex.ImGui` is a package that integrates the popular Dear ImGui library with the HelixToolkit.Nex 3D graphics engine. It provides a renderer for displaying ImGui-based user interfaces within a Vulkan-based rendering pipeline, leveraging the HelixToolkit.Nex ecosystem for efficient GPU resource management and rendering.

## Overview

The `HelixToolkit.Nex.ImGui` package is designed to seamlessly integrate ImGui into applications built with the HelixToolkit.Nex engine. It provides an `ImGuiRenderer` class that handles the rendering of ImGui draw data using Vulkan, and an `ImGuiConfig` class for configuring font settings. This package is ideal for developers who want to add real-time, interactive user interfaces to their 3D applications.

Key features:
- Efficient rendering of ImGui draw data using Vulkan.
- Support for custom fonts and scaling through configuration.
- Integration with the HelixToolkit.Nex rendering pipeline.

## Key Types

| Type              | Description                                                                 |
|-------------------|-----------------------------------------------------------------------------|
| `ImGuiConfig`     | Configuration class for ImGui settings, including font path and size.      |
| `ImGuiRenderer`   | Main class responsible for rendering ImGui draw data using Vulkan.         |

### `ImGuiConfig`

The `ImGuiConfig` class allows you to configure ImGui settings such as font path and font size.

#### Properties
- `FontPath` (`string`): Path to the font file to be used by ImGui. Defaults to an empty string, which uses the default ImGui font.
- `FontSizeInPixel` (`int`): Font size in pixels. Defaults to `16`.

### `ImGuiRenderer`

The `ImGuiRenderer` class is responsible for rendering ImGui draw data. It manages the ImGui context, compiles shaders, creates the rendering pipeline, and uploads font textures to the GPU.

#### Constructor
```csharp
public ImGuiRenderer(IContext context, ImGuiConfig config)
```
- `context`: The HelixToolkit.Nex rendering context.
- `config`: An instance of `ImGuiConfig` for configuring ImGui settings.

#### Properties
- `IContext Context`: The rendering context used by the renderer.
- `float DisplayScale`: Scale factor for the ImGui display. Default is `1.0f`.
- `TextureResource FontTexture`: The GPU resource for the ImGui font texture.
- `nint ImGuiContext`: The native ImGui context handle.

#### Methods
- `bool Initialize(Format targetFormat)`: Initializes the ImGui renderer with the specified target format. Returns `true` if successful.
- `void SetFont(string fontPath)`: Sets the font for ImGui using the specified font file path.

## Usage Examples

### Basic Setup and Rendering
```csharp
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.ImGui;
using ImGuiNET;

// Create a rendering context (assume `context` is already initialized)
IContext context = ...;

// Configure ImGui
var config = new ImGuiConfig
{
    FontPath = "path/to/font.ttf",
    FontSizeInPixel = 18
};

// Create the ImGui renderer
using var imguiRenderer = new ImGuiRenderer(context, config);

// Initialize the renderer with the target format
if (!imguiRenderer.Initialize(Format.B8G8R8A8_UNORM))
{
    Console.WriteLine("Failed to initialize ImGuiRenderer.");
    return;
}

// Main rendering loop
while (true)
{
    // Start a new ImGui frame
    ImGuiNET.ImGui.NewFrame();

    // Create a simple UI
    ImGuiNET.ImGui.Begin("Hello, ImGui!");
    ImGuiNET.ImGui.Text("This is a simple ImGui window.");
    ImGuiNET.ImGui.End();

    // Render the ImGui frame
    ImGuiNET.ImGui.Render();
    imguiRenderer.Render(ImGuiNET.ImGui.GetDrawData());
}
```

### Changing the Font at Runtime
```csharp
// Change the font during runtime
imguiRenderer.SetFont("path/to/another-font.ttf");
```

## Architecture Notes

The `HelixToolkit.Nex.ImGui` package is built on the following architectural principles and dependencies:

- **Integration with HelixToolkit.Nex**: The `ImGuiRenderer` relies on the `IContext` interface from `HelixToolkit.Nex.Graphics` for Vulkan resource management and rendering.
- **Shader Compilation**: The package uses the `ShaderCompiler` from `HelixToolkit.Nex.Shaders` to compile GLSL shaders for ImGui rendering.
- **Push Constants**: The renderer uses Vulkan push constants to pass transformation matrices and other data to the shaders.
- **Font Texture Management**: The ImGui font texture is uploaded to the GPU as a `TextureResource` for efficient sampling during rendering.
- **Dependency on ImGui.NET**: The package uses the `ImGui.NET` library to interact with the Dear ImGui API.

This package is a crucial component for developers looking to add real-time, interactive user interfaces to their HelixToolkit.Nex-based applications.
```
