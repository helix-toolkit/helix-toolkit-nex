```markdown
# HelixToolkit.Nex.Engine

HelixToolkit.Nex.Engine is a powerful 3D graphics engine implemented in C# that leverages the Vulkan API for high-performance rendering. It provides a comprehensive set of tools for building and managing 3D scenes, including camera control, lighting, and mesh management. The engine is designed to be modular and extensible, making it suitable for a wide range of applications from games to simulations.

## Overview

HelixToolkit.Nex.Engine is a core component of the HelixToolkit.Nex suite, responsible for managing the rendering pipeline and scene graph. It integrates with the Vulkan API to provide efficient GPU-based rendering and supports advanced features such as:
- Reverse-Z projection matrices for improved depth precision.
- Forward Plus light culling for efficient lighting calculations.
- GPU-based frustum and instance culling for performance optimization.
- An Entity Component System (ECS) architecture for flexible scene management.
- A Render Graph for managing the execution order of rendering nodes.

## Key Types

| Type                             | Description                                                                 |
|----------------------------------|-----------------------------------------------------------------------------|
| `Engine`                         | The main coordinator for the 3D rendering engine.                           |
| `RenderContext`                  | Holds per-viewport state such as window size and camera parameters.         |
| `WorldDataProvider`              | Provides scene data to the engine, managing ECS-to-GPU data pipelines.      |
| `FirstPersonCameraController`    | A camera controller for first-person navigation.                           |
| `WalkaroundCameraController`     | A specialized first-person camera controller with inverted controls.       |
| `OrbitCameraController`          | A camera controller for orbiting around a target point.                    |
| `PanZoomCameraController`        | A camera controller for panning and zooming, suitable for orthographic views.|
| `TurntableCameraController`      | A camera controller for turntable-style rotation around a fixed axis.      |
| `Camera`                         | Base class for camera implementations, supporting view and projection matrices. |
| `OrthographicCamera`             | A camera with an orthographic projection.                                  |
| `PerspectiveCamera`              | A camera with a perspective projection.                                    |
| `DirectionalLightInfo`           | Represents a directional light in the scene.                               |
| `RangeLightInfo`                 | Represents a point or spot light in the scene.                             |
| `DirectionalLightNode`           | Represents a directional light node in the scene graph.                    |
| `PointLightNode`                 | Represents a point light node in the scene graph.                          |
| `SpotLightNode`                  | Represents a spotlight node in the scene graph.                            |
| `EngineBuilder`                  | Fluent builder for creating and configuring an `Engine` instance.          |
| `BillboardData`                  | Manages billboard entities and their data for rendering.                   |
| `DynamicMeshDrawData`            | Represents dynamic mesh draw data.                                         |
| `BillboardNode`                  | Represents a node in the scene graph that contains billboard geometry.     |
| `CameraExtensions`               | Provides extension methods for camera operations, such as focusing on targets. |
| `DrawStreamBase`                 | Abstract base class for managing GPU draw streams with material grouping.  |
| `PointDrawStream`                | Manages point draw commands for rendering point clouds.                    |
| `PointDrawStreamRegistry`        | Registry for managing point draw streams.                                  |

## Usage Examples

### Creating and Initializing the Engine

```csharp
using var engine = EngineBuilder.Create(context)
    .WithDefaultNodes()
    .Build();

var viewport = engine.CreateRenderContext();
viewport.Initialize();
var worldData = engine.CreateWorldDataProvider();
worldData.Initialize();

// In game loop:
viewport.WindowSize = new Size(width, height);
viewport.CameraParams = camera.ToCameraParams(aspectRatio);
engine.Render(viewport, worldData);
```

### Using a First-Person Camera Controller

```csharp
var camera = new PerspectiveCamera
{
    Position = new Vector3(0, 0, 10),
    Target = Vector3.Zero
};
var controller = new FirstPersonCameraController(camera)
{
    LookSensitivity = 0.005f,
    MoveSpeed = 10f,
    InvertX = false,
    InvertY = false
};

// Update loop
controller.SetMovementInput(forward: true);
controller.Update(deltaTime);
```

### Using a Walkaround Camera Controller

```csharp
var camera = new PerspectiveCamera
{
    Position = new Vector3(0, 0, 10),
    Target = Vector3.Zero
};
var controller = new WalkaroundCameraController(camera);

// Update loop
controller.SetMovementInput(forward: true);
controller.Update(deltaTime);
```

### Picking Objects in the Scene

```csharp
var pickingResult = renderContext.Pick(mouseX, mouseY);
if (pickingResult != null)
{
    Console.WriteLine($"Picked entity ID: {pickingResult.Entity.Id}");
}
```

### Creating Billboards

```csharp
var billboard = engine.CreateBillboard(
    BuildinFontAtlas.Default,
    "Hello World",
    12f,
    new Color4(1, 1, 1, 1),
    background: new Color4(0, 0, 0, 0.5f),
    cullDistance: 100f
);
```

### Focusing the Camera on a Target

```csharp
camera.FocusOn(new Vector3(0, 0, 0), 10f);
```

## Architecture Notes

- **Entity Component System (ECS):** The engine uses an ECS architecture based on the Arch ECS library, allowing for flexible and efficient scene management.
- **Render Graph:** The engine employs a Render Graph to manage the execution order of rendering nodes, ensuring optimal performance and resource management.
- **Reverse-Z Projection:** The engine uses reverse-Z projection matrices to improve depth buffer precision, reducing artifacts in large scenes.
- **Forward Plus Lighting:** Forward Plus light culling is used to efficiently manage lighting calculations, supporting a large number of dynamic lights.
- **Integration with Vulkan:** The engine is built on top of the Vulkan API, providing low-level access to GPU resources and enabling high-performance rendering.

## Recent Additions

### Updates to Camera Controllers

- Added `FocusOn(Vector3 target, float? distance = null)` method to `ICameraController` and all implementing classes (`FirstPersonCameraController`, `OrbitCameraController`, `PanZoomCameraController`, `TurntableCameraController`) to re-center the camera on a new target point and optionally adjust the distance.

### New `CameraExtensions` Class

- Provides extension methods for focusing cameras on specific targets or bounding volumes, enhancing usability for camera manipulation.

### Changes in `MeshDrawStream`

- Introduced `DrawStreamType` and `DrawStreamVariants` to replace `DrawStreamCategory`.
- `MeshDrawStream` now uses `DrawStreamType` and `DrawStreamVariants` for entity-to-slot lookup.
- Improved handling of dynamic data with `RingElementBuffer`.

### Enhancements in `MeshDrawStreamRegistry`

- Streams are now organized by `DrawStreamType` and `DrawStreamName`.
- Improved stream management and lookup efficiency.

### New Methods in `Engine`

- `ProcessEvents()`: Processes pending events in the engine's event bus.
- `EnsureResources(RenderContext context)`: Ensures that all necessary resources are available for rendering, processing events, and preparing the render graph.
- `Submit(ICommandBuffer commandBuffer, in TextureHandle present)`: Submits a command buffer for execution on the GPU.
- `Submit(ICommandBuffer commandBuffer, in TextureHandle present, KeyedMutexSyncInfo syncInfo)`: Submits a command buffer with synchronization information.

### New Overloads for `RenderOffscreen`

- `RenderOffscreen(RenderContext renderContext, IRenderDataProvider dataProvider, TextureHandle target)`: Executes the render graph into an offscreen target without presenting.
- `RenderOffscreen(RenderContext renderContext, IRenderDataProvider dataProvider, string targetName)`: Executes the render graph into an offscreen target specified by name, without presenting.

### `EngineBuilder` Enhancements

- `RenderToCustomTarget(Format targetFormat)`: Configures the engine to render to a custom target format.
- Improved interop support with `WithWpf()` and `WithWinUI()` methods for WPF and WinUI applications.
- New methods for enabling specific rendering features: `WithBillBoard()`, `WithTransparent()`, `WithFXAA()`, `WithSMAA()`, `WithBloom()`, `WithFPS()`.

### Buffer Management Updates

- Replaced `ElementBuffer` with `RingElementBuffer` for `MeshDrawData` and `RangeLightData` to improve buffer management and performance.
- `RingElementBuffer` allows for efficient handling of dynamic data by rotating through buffer slots, minimizing GPU stalls.

### Light Data Management

- Updated `DirectionalLightData` and `RangeLightData` to use `RingFixSizeBuffer` and `RingElementBuffer` respectively, improving resource management and update efficiency.

### New `PointDrawStream` and `PointDrawStreamRegistry`

- Introduced `PointDrawStream` for managing point draw commands, replacing the removed `PointCloudData`.
- `PointDrawStreamRegistry` manages point draw streams, organizing them by `DrawStreamType` and `DrawStreamName`.

### New Light Nodes

- `DirectionalLightNode`, `PointLightNode`, and `SpotLightNode` added to represent light sources in the scene graph, allowing for more intuitive scene management and light manipulation.

### Renaming of Light Components

- `DirectionalLightComponent` has been renamed to `DirectionalLightInfo`.
- `RangeLightComponent` has been renamed to `RangeLightInfo`.
```
