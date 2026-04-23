```markdown
# HelixToolkit.Nex.Shaders

The `HelixToolkit.Nex.Shaders` package is a comprehensive library for shader management and generation within the HelixToolkit-Nex 3D graphics engine. It provides tools and utilities for creating, compiling, and caching shaders, supporting advanced rendering techniques such as Forward Plus light culling, GPU-based frustum culling, and various post-processing effects.

## Overview

This package is integral to the HelixToolkit-Nex engine, facilitating the creation and management of shaders that are essential for rendering 3D graphics using the Vulkan API. It supports:
- Shader generation for various rendering techniques.
- Management of shader compilation and caching.
- Utilities for shader customization and optimization.

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `BuildFlags`                  | Contains constants for shader build options, such as excluding mesh properties. |
| `ForwardPlusLightCulling`     | Generates compute shaders for Forward+ light culling.                       |
| `GpuFrustumCulling`           | Provides methods for generating compute shaders for GPU-based frustum culling. |
| `Helpers`                     | Utility methods for creating and updating culling constants.                |
| `BloomMode`                   | Enum for selecting the active stage of the bloom shader.                    |
| `HighlightMode`               | Enum for selecting the active stage of the border-highlight shader.         |
| `PBRShadingMode`              | Enum for different PBR shading modes.                                       |
| `SampleTextureMode`           | Enum for texture sampling modes, including debug modes.                     |
| `SmaaMode`                    | Enum for selecting the active stage of the SMAA shader.                     |
| `ToneMappingMode`             | Enum for different tone mapping modes.                                      |
| `ShaderBuilder`               | Processes shader source code and includes necessary headers.                |
| `ShaderCache`                 | Manages a cache of processed shader sources for efficient reuse.            |
| `ShaderCompiler`              | High-level API for building shaders with automatic header inclusion.        |

## Usage Examples

### Generating a Forward+ Light Culling Shader

```csharp
var config = ForwardPlusLightCulling.Config.Default;
string shaderSource = ForwardPlusLightCulling.GenerateComputeShader(config);
Console.WriteLine(shaderSource);
```

### Compiling a Shader with Custom Options

```csharp
var options = new ShaderBuildOptions
{
    StripComments = true,
    Defines = new Dictionary<string, string> { { "EXCLUDE_MESH_PROPS", "" } }
};

var compiler = new ShaderCompiler();
var result = compiler.CompileFragmentShader("shader source code here", options);

if (result.Success)
{
    Console.WriteLine("Shader compiled successfully.");
}
else
{
    Console.WriteLine("Shader compilation failed: " + string.Join("\n", result.Errors));
}
```

### Using the Shader Cache

```csharp
var cache = new ShaderCache(maxEntries: 100, expirationTime: TimeSpan.FromMinutes(10));
var cacheKey = ShaderCache.GenerateCacheKey("shader source", new ShaderBuildOptions(), ShaderStage.Fragment);

if (!cache.TryGet(cacheKey, out var entry))
{
    // Compile and cache the shader
    var compiler = new ShaderCompiler();
    var result = compiler.CompileFragmentShader("shader source code");
    if (result.Success)
    {
        cache.Set(cacheKey, result.Source, "source hash");
    }
}
```

## Architecture Notes

- **Design Patterns**: The package utilizes the Builder pattern for shader compilation, allowing for a fluent API to configure shader build options.
- **Dependencies**: It relies on other HelixToolkit-Nex packages for ECS and rendering management.
- **Shader Management**: The package includes a robust caching mechanism to optimize shader compilation times and resource usage.
- **Shader Generation**: Provides utilities for generating GLSL shader code with support for various rendering techniques and optimizations.
```
