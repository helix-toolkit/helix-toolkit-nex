```markdown
# HelixToolkit.Nex.Rendering

HelixToolkit.Nex.Rendering is a comprehensive rendering package designed for the HelixToolkit-Nex 3D graphics engine. It provides a robust set of tools and components for rendering 3D scenes using the Vulkan API, leveraging advanced techniques such as Forward Plus lighting, GPU-based culling, and post-processing effects.

## Overview

HelixToolkit.Nex.Rendering is responsible for managing the rendering pipeline of the HelixToolkit-Nex engine. It integrates with the engine's Entity Component System (ECS) to render 3D scenes efficiently. Key features include:
- **Forward Plus Lighting**: Efficiently handles a large number of lights using tiled light culling.
- **GPU-Based Culling**: Performs frustum and instance culling on the GPU to reduce CPU load.
- **Post-Processing Effects**: Supports a variety of post-processing effects such as Bloom, FXAA, and Tone Mapping.
- **Render Graph**: Manages the execution order of rendering nodes to optimize performance and resource usage.

## Key Types

| Type                             | Description                                                                 |
|----------------------------------|-----------------------------------------------------------------------------|
| `IIndexable`                     | Interface for components that can be indexed.                               |
| `MeshDrawInfo`                   | Represents a mesh render component with geometry and material associations. |
| `PointDrawInfo`                  | Describes a point cloud attached to an entity.                              |
| `BillboardDrawInfo`              | Describes one or more billboards attached to an entity.                     |
| `LineDrawInfo`                   | Describes line geometry attached to an entity.                              |
| `ForwardPlusLightCullingNode`    | Performs tiled Forward+ light culling.                                      |
| `FrustumCullNode`                | Executes GPU-based frustum culling, including line and point culling.       |
| `BillboardCullNode`              | Performs culling operations on billboards based on screen size and distance.|
| `RenderContext`                  | Manages rendering state and resources for a frame.                          |
| `RenderGraph`                    | Organizes and executes rendering nodes in a defined order.                  |
| `PostEffect`                     | Base class for post-processing effects.                                     |
| `Renderer`                       | Manages the lifecycle and execution of render nodes.                        |
| `RenderParams`                   | Contains render parameters including background color and other settings.   |
| `BoundingBoxPostEffect`          | Renders wireframe bounding boxes for debugging purposes.                    |
| `WireframePostEffect`            | Renders wireframe overlays on meshes with customizable color and depth bias.|
| `BorderHighlightPostEffect`      | Renders colored outlines around mesh silhouettes.                           |
| `RenderGraphResourceAllocationException` | Exception thrown when a render-graph resource fails to allocate. |
| `PickingContext`                 | Manages GPU-based picking operations by reading entity information from a texture.|
| `IInstancingManager`             | Interface for managing instancing resources, including lifecycle and GPU uploads.|
| `InstancingManager`              | Manages a pool of `Instancing` objects, providing lifecycle and GPU resource management.|
| `SMAANode`                       | Performs Subpixel Morphological Anti-Aliasing (SMAA) with configurable quality and debug modes. |
| `FXAANode`                       | Performs Fast Approximate Anti-Aliasing (FXAA) with configurable quality settings. |
| `BillboardHelper`                | Provides factory helpers for creating image/icon billboards.                |
| `GizmoManager`                   | Manages gizmo instances, including creation, binding, and manipulation.     |
| `SysEncodingKind`                | Enumerates alternate encodings for system-specific rendering.               |
| `SsaoMath`                       | Provides GPU-free reference implementations for SSAO effect math.           |
| `EnvironmentMapConfig`           | Configures environment-map (skybox) rendering for a render context.         |
| `EnvironmentMapNode`             | Renders an HDR environment cubemap as the scene background (skybox).        |

## Recent Changes

### New Features

- **EnvironmentMapConfig**: Added to configure environment-map (skybox) rendering per `RenderContext`.
- **EnvironmentMapNode**: Added to render an HDR environment cubemap as the scene background.
- **BillboardHelper**: Added to provide factory helpers for creating image/icon billboards.
- **GizmoManager Enhancements**: Added binding and drag manipulation capabilities for gizmo instances.
- **GizmoManager.Binding**: Introduced to manage runtime target-binding for gizmo instances.
- **GizmoManager.Drag**: Added drag-manipulation lifecycle for gizmos, including begin, update, and end drag operations.
- **GizmoManager.Factory**: Enhanced to support gizmo creation, updating, and removal with caching for handle sets.
- **BillboardCullNode**: Added to perform culling operations on billboards based on screen size and distance.
- **DrawStream Enhancements**: Introduced `DrawStreamType` and `DrawStreamVariants` for more precise control over draw stream characteristics.
- **RenderGraphResourceAllocationException**: Added to handle resource allocation failures in the render graph.
- **Material Type Name Properties**: Updated `LineDrawInfo` and `PointDrawInfo` to use `LineMaterialTypeName` and `PointMaterialTypeName` respectively for material lookup.
- **PickingContext**: Enhanced to use `GetBufferData` for reading results, improving resource management.
- **Barrier Presets**: Introduced `BarrierPreset` for more precise control over buffer synchronization.
- **InstancingManager**: Added to manage instancing resources, including lifecycle, eventing, GPU-upload, and deferred-removal.
- **Instancing**: Updated to support dynamic and static instancing modes with ring buffers to prevent GPU stalls.
- **SysEncodingKind**: Added to enumerate alternate encodings for system-specific rendering.
- **SsaoMath**: Added to provide GPU-free reference implementations of SSAO effect math, including parameter validation and depth/normal reconstruction.
- **LineDrawInfo Enhancements**: Added `TextureIndex` and `SamplerIndex` properties for bindless texture and sampler support.

### Removed Features

- **PointCloudDrawInfo**: Removed and replaced by `PointDrawInfo`.
- **PointCullNode**: Removed and its functionality integrated into `FrustumCullNode`.

### Updated Sampler Handling

- **SamplerRef**: Replaced `SamplerResource` with `SamplerRef` for improved resource management and validation.
- **Validation**: Added validation checks for sampler creation to ensure resources are correctly initialized.

### FrustumCullNode

- **Buffer Management**: Updated to include `BufferMeshInfo` as an input and output dependency for better resource tracking and management.
- **Render Setup**: Added `OnSetupRender` method to manage buffer dependencies.
- **Camera Frustum**: Now uses `CameraFrustum` from `RenderContext` for culling operations.

### ForwardPlusLightCullingNode

- **Render Setup**: Added `OnSetupRender` method to manage texture dependencies.
- **Light Count Limiting**: Added logic to cap the number of lights processed to prevent out-of-range indices.

### MeshDrawInfo

- **Variants Property**: Renamed from `Category` to `Variants` to determine the draw stream category based on instancing, hitability, and dynamic state.

### Draw Stream Enhancements

- **DrawStreamType and DrawStreamVariants**: Introduced to replace `DrawStreamCategory` for more precise control over draw stream characteristics.
- **IDrawStream**: Updated `GetMaterialTypes` method for zero-allocation material type enumeration.
- **IDrawStreamRegistry**: Added `GetStreamsCore` method for zero-allocation stream enumeration.
- **MeshDrawStreamEnumerable**: Introduced for efficient enumeration of draw streams without heap allocations.

### CameraParams

- **Equality and Identity**: Added `Equals` and `IsIdentity` methods for `CameraParams` to facilitate comparison and identity checks.

### BorderHighlightPostEffect

- **Optimization**: Improved grouping logic for color passes to minimize the number of passes required.

### WireframePostEffect

- **Optimization**: Simplified entity retrieval logic for wireframe draws.

## Usage Examples

### Setting Up a Render Graph

```csharp
var services = new ServiceCollection();
var renderGraph = new RenderGraph(services.BuildServiceProvider());

renderGraph
    .AddTexture("MainColor", p => p.Context.Context.CreateTexture2D(...))
    .AddPass(
        "DepthPass",
        inputs: [new RenderResource("MainColor", ResourceType.Texture)],
        outputs: [new RenderResource("DepthBuffer", ResourceType.Texture)],
        onSetup: res => { /* Setup code here */ }
    );
```

### Adding a Mesh Component

```csharp
var meshDrawInfo = new MeshDrawInfo(
    geometry: myGeometry,
    materialProperties: myMaterialProperties,
    instancing: myInstancing
);
```

### Adding a Billboard Component

```csharp
var billboardDrawInfo = new BillboardDrawInfo
{
    BillboardGeometry = new BillboardGeometry(),
    Color = new Color4(1f, 1f, 1f, 1f),
    FixedSize = false,
    CullDistance = 100f // Set culling distance
};
```

### Creating an Image Billboard

```csharp
var texture = new TextureRef(...);
var sampler = new SamplerRef(...);
var billboard = BillboardHelper.CreateImageBillboard(
    texture,
    sampler,
    width: 100f,
    height: 100f,
    tint: new Color4(1f, 1f, 1f, 1f),
    fixedSize: true
);
```

### Applying Post-Processing Effects

```csharp
var postEffectsNode = new PostEffectsNode();
postEffectsNode.AddEffect(new Bloom { Threshold = 0.8f, Intensity = 2.0f });
postEffectsNode.AddEffect(new Fxaa { Quality = FxaaQuality.Medium });
```

### Visualizing Bounding Boxes

```csharp
var boundingBoxEffect = new BoundingBoxPostEffect
{
    UseDepthTest = true
};
boundingBoxEffect.Apply(renderResources, ref readSlot, ref writeSlot);
```

### Highlighting Mesh Borders

```csharp
var borderHighlightEffect = new BorderHighlightPostEffect();
borderHighlightEffect.Apply(renderResources, ref readSlot, ref writeSlot);
```

### Configuring Environment Map

```csharp
var environmentMapConfig = new EnvironmentMapConfig
{
    Texture = myCubemapTexture,
    Intensity = 1.5f,
    RotationY = MathF.PI / 4,
    Blur = 0.5f
};
renderContext.EnvironmentMap = environmentMapConfig;
```

## Architecture Notes

- **Entity Component System (ECS)**: The rendering engine uses an ECS architecture to manage entities and their components, allowing for flexible and efficient scene management.
- **Render Graph**: The render graph organizes rendering tasks into nodes, ensuring that resources are used efficiently and that tasks are executed in the correct order.
- **Reverse-Z**: The engine uses a reverse-Z depth buffer to improve precision in depth testing.
- **Post-Processing**: Post-processing effects are modular and can be added or removed easily, allowing for customizable rendering pipelines.

HelixToolkit.Nex.Rendering is designed to be both powerful and flexible, providing developers with the tools they need to create high-performance 3D applications.
```
