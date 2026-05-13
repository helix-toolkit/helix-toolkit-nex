```markdown
# HelixToolkit.Nex.Material

The `HelixToolkit.Nex.Material` package provides a comprehensive framework for managing materials in the HelixToolkit-Nex 3D graphics engine. It supports the creation, management, and rendering of Physically Based Rendering (PBR) materials, custom material buffers, point materials, and billboard materials, integrating seamlessly with the Vulkan API through the HelixToolkit-Nex engine.

## Overview

The `HelixToolkit.Nex.Material` package is responsible for:
- Managing PBR materials and their properties.
- Supporting custom material buffers for advanced shading techniques.
- Handling point materials for point cloud rendering.
- Managing billboard materials for efficient billboard rendering.
- Integrating with the HelixToolkit-Nex ECS and Render Graph systems.

This package plays a crucial role in the rendering pipeline, providing the necessary abstractions and implementations to handle various material types and their associated shaders.

## Key Types

| Type | Description |
|------|-------------|
| `PBRMaterial` | Base class for all materials used in rendering. |
| `CustomBufferPBRMaterial<T>` | Extends `PBRMaterial` to support custom buffers. |
| `CustomMaterialBuffer<T>` | Manages GPU-side storage buffers for custom material properties. |
| `ICustomMaterialBuffer` | Interface for managing custom material buffers. |
| `IPBRMaterialManager` | Interface for managing PBR materials and their pipelines. |
| `PBRMaterialManager` | Implements `IPBRMaterialManager` for managing PBR materials. |
| `PBRMaterialProperties` | Manages PBR material properties for a single material instance. |
| `PBRMaterialShaderBuilder` | Builds shader code for materials with GLSL integration. |
| `PBRMaterialTypeRegistry` | Global registry for material types and their shader implementations. |
| `PointMaterialManager` | Manages point cloud render pipelines. |
| `PointMaterialRegistry` | Registry for point material types and their shader implementations. |
| `BillboardMaterialManager` | Manages billboard render pipelines. |
| `BillboardMaterialRegistry` | Registry for billboard material types and their shader implementations. |
| `MaterialTypeId` | Represents a unique identifier for material types. |
| `MaterialPropertyCreator` | Facilitates creation and configuration of material properties. |
| `GraphicsSettings` | Provides static render settings including formats and frame configurations. |

## Usage Examples

### Creating a Custom PBR Material

```csharp
// Define a custom struct matching the GLSL layout
[StructLayout(LayoutKind.Sequential)]
public struct CustomProps
{
    public Vector4 Color;
    public float Intensity;
}

// Create a custom buffer for the material
var customBuffer = new CustomMaterialBuffer<CustomProps>(context, "CustomMaterial");

// Set properties
customBuffer.Properties = new CustomProps { Color = new Vector4(1, 0, 0, 1), Intensity = 0.5f };

// Update buffer and use in rendering
customBuffer.Update();
MeshDrawPushConstant.customMaterialBufferAddress = customBuffer.GpuAddress;
```

### Managing PBR Materials

```csharp
var materialManager = new PBRMaterialManager(context, propertyManager);

// Create a new PBR material
var materialCreator = materialManager.CreateMaterial("MyMaterial", name => new PBRMaterial(name));

// Retrieve and modify material properties
var materialProperties = materialCreator.Create();
materialProperties.Albedo = new Color4(1, 0, 0, 1);
materialProperties.Metallic = 0.5f;
materialProperties.BumpScale = 1.0f;
materialProperties.DisplaceScale = 0.5f;
```

### Managing Billboard Materials

```csharp
var billboardManager = new BillboardMaterialManager(context, shaderRepository);

// Create pipelines for all registered billboard materials
int createdPipelines = billboardManager.CreatePipelinesFromRegistry();

// Retrieve a pipeline handle for a specific material type
var pipelineHandle = billboardManager.GetPipeline(new MaterialTypeId(1)); // Example ID
```

## Architecture Notes

- **Design Patterns**: The package utilizes the Factory and Singleton patterns for material creation and management.
- **Dependencies**: Relies on `HelixToolkit.Nex.Graphics` for rendering context and pipeline management, and `HelixToolkit.Nex.Shaders` for shader compilation and management.
- **Integration**: Works with the HelixToolkit-Nex ECS and Render Graph systems to ensure efficient material management and rendering.

The `HelixToolkit.Nex.Material` package is a vital component of the HelixToolkit-Nex engine, providing robust and flexible material management capabilities essential for advanced 3D rendering applications.
```
