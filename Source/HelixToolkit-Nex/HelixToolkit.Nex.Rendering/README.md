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
| `SkeletonPostEffect`             | Draws the bones of skinned meshes as lines, arrows or octahedra (`BoneStyle`), optionally without depth test. |
| `SkeletonPostEffect.SkeletonOverlay` | Marks a skinned mesh entity for bone visualization and sets the bone color. |
| `MorphPostEffect`                | Draws the morph target displacement of each vertex of morphed meshes as a line (un-morphed to morphed position, skinned when the mesh is skinned); `LengthScale`, `MinLength`, `UseDepthTest`. |
| `MorphPostEffect.MorphOverlay`   | Marks a morphed mesh entity for displacement visualization and sets the line color. |
| `BoneStyle`                      | Enumerates the shapes used to draw bones: `Line`, `Arrow`, and `Octahedral`. |
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
| `SkyAtmosphereConfig`            | Configures the dynamic sky atmosphere (scattering media, sun, units) for a render context. |
| `SkyAtmosphereNode`              | Renders a raymarched sky atmosphere as the scene background in place of the environment map. |
| `RenderSpace`                    | Specifies the space in which a draw primitive's size/thickness is measured. |
| `LineCap`                        | Shape of unconnected line segment ends: `Butt`, `Square`, `Round`.          |
| `LineJoin`                       | How connected line segments are joined: `None`, `Miter`, `Bevel`, `Round`.  |
| `Skeleton`                       | Shared skinning asset holding the inverse bind matrices of a joint hierarchy. |
| `Skinning`                       | Binds a skinned mesh to its joint entities and owns the compute skinning buffers. |
| `Morphing`                       | Binds a mesh with morph targets to its `MorphWeights` and owns the weight buffer (and the output buffers when the mesh is not skinned). |
| `SkinningNode`                   | Deforms all skinned and morphed meshes on the GPU (palette + vertex compute passes; morph targets, then skinning) in `RenderStage.Prepare`. |
| `ISkinningData`                  | Provides the per-frame deformation dispatches (`SkinningDispatch`) to `SkinningNode`. |
| `SkinningDispatch`               | Record struct pairing a `Skinning` and/or `Morphing` with its bind-pose `Geometry` for one deformation dispatch. |

## Recent Changes

### New Features

- **Sky atmosphere**: `SkyAtmosphereConfig` (`RenderContext.SkyAtmosphere`) and `SkyAtmosphereNode` add a dynamic, raymarched sky: Rayleigh, Mie and ozone single scattering over a flat ground with world +Y as up, lengths in kilometers and world units converted via `KmPerUnit` (default 0.001, one unit = one meter). While enabled, the node draws the background in place of `EnvironmentMapNode` (which skips itself), the PBR, Flat and Unlit shading models apply aerial perspective (distance haze) through `applyAtmosphere()`, and directional light 0 is used as the sun and tinted by the sunlight transmittance (`SunFromDirectionalLight`); its intensity also drives the sky's sun intensity, scaled by `DirectionalLightIntensityScale` (`SunIntensityFromDirectionalLight`), and its colour tints the sky and haze (`SunColorFromDirectionalLight`; otherwise `SunColor`). `ShowEnvironmentMapAsSpace` draws the environment cubemap behind the atmosphere (e.g. a star field). Below the horizon the background fades from the horizon sky into `GroundColor` (lit by the horizon radiance) over `GroundBlendAngle`. The CAD and debug shading models, and point/line/billboard materials, are not hazed.
- **Line caps and joins**: `LineDrawInfo` gained `Cap` (`LineCap`, default `Butt`), `Join` (`LineJoin`, default `Miter`) and `MiterLimit` (default 4, SVG `stroke-miterlimit` semantics). Line geometry stays a list of disjoint 2-vertex segments; the line shader treats consecutive segments as connected when the end vertex of one equals the start vertex of the next (and the last segment with the first, for closed loops). Connected segments are split along the joint's angle bisector, so translucent lines no longer overlap or leave gaps at corners. Edge anti-aliasing and the cap/join shape are applied in the line fragment template's `main()` for every line material, so custom `OutputColorImplementation` code no longer needs to feather the edge. Set `Join = LineJoin.None` to render every segment independently.
- **LineDrawInfo defaults**: Added an explicit parameterless constructor so `new LineDrawInfo { ... }` applies the documented property defaults (`LineColor = White`, `LineThickness = 1`, `Join = Miter`, …) instead of zero-initializing the struct.
- **RenderSpace**: Introduced to specify the space in which a draw primitive's size/thickness is measured, replacing the `FixedSize` boolean with a more descriptive enum.
- **BillboardHelper**: Updated to use `RenderSpace` instead of `FixedSize` for determining the size interpretation of billboards.
- **LineDrawInfo**: Added `RenderSpace` property to determine how line thickness is interpreted.
- **PointDrawInfo**: Added `RenderSpace` property to determine how point size is interpreted.
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
- **SkeletonPostEffect**: Added to visualize skeleton bones of skinned meshes as lines, arrows, or octahedra. Bones are drawn on top of the scene by default; `UseDepthTest`, `Style` (`BoneStyle`), and `HeadSize` control depth testing and bone shape.

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

### EnvironmentMapConfig

- **Master Switch Scope**: `Enabled` now also turns off PBR cubemap reflections. `ShouldRenderCubeMap` returns `true` only when `Enabled` and `RenderCubeMap` are both set and a valid cubemap texture is assigned. `RenderCubeMap` no longer operates independently of `Enabled`.

### EnvironmentMapNode

- **Global Wireframe**: The node is skipped when `RenderParams.EnableGlobalWireframe` is enabled, because the depth pre-pass does not run in that mode and the background would otherwise be drawn over the wireframe lines.
- **Sky Atmosphere**: The node is skipped while `RenderContext.SkyAtmosphere.Enabled` is set; `SkyAtmosphereNode` draws the background instead. The cubemap still drives PBR reflections.

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

### Adding a Skinned Mesh

Bone skinning runs in a compute pass ahead of rendering. The geometry must carry per-vertex
`JointInfluences` (four joints and weights per vertex); the joints are regular scene nodes whose
`WorldTransform` drives each bone.

```csharp
var skeleton = new Skeleton(inverseBindMatrices);             // shared, one matrix per joint
var skinning = new Skinning(skeleton, jointNodes.Select(n => n.Entity).ToArray());
var meshDrawInfo = new MeshDrawInfo(
    geometry: skinnedGeometry,                                // geometry.JointInfluences populated
    materialProperties: myMaterialProperties,
    skinning: skinning
);
```

- `SkinningNode` computes `palette[j] = jointMatrix[j] * inverseBind[j]` and then skins positions,
  normals and tangents (linear blend skinning) into buffers owned by the `Skinning`. Mesh draw
  commands reference those buffers (`MeshDraw.skinnedVertexBufferAddress`), so every pass
  (depth, opaque, transparent, picking, wireframe) reads the skinned vertices without re-skinning.
- Joint matrices are uploaded relative to the mesh node, so the mesh node's own transform does not
  affect the pose (glTF semantics).
- Skinned meshes are never frustum culled (`MeshDrawInfo.IsCullable` is `false`) because their
  bind-pose bounds do not cover the animated pose.
- A `Skinning` holds the output of one geometry and must not be shared between mesh entities.
  Instanced skinned meshes share one pose across all instances.
- `Skinning` and `Skeleton` own GPU buffers and must be disposed by their owner.
- Each frame, skinned meshes are exposed to the render graph through `IRenderDataProvider.SkinningData`,
  an `ISkinningData` whose `Dispatches` supply one `SkinningDispatch` per skinned mesh. It is `null`
  when no skinning data provider is registered.

### Adding a Mesh with Morph Targets

Morph targets run in the same compute pass as skinning, before it (glTF order). The geometry carries
the targets (`Geometry.MorphTargets`); the weights are a `MorphWeights` that animation channels or
your code change at runtime.

```csharp
var weights = new MorphWeights([0f, 0.5f]);                   // one weight per morph target
var meshDrawInfo = new MeshDrawInfo(
    geometry: morphGeometry,                                  // geometry.MorphTargets populated
    materialProperties: myMaterialProperties,
    morphing: new Morphing(weights)
);
weights[0] = 1f;                                              // re-uploaded on the next frame
```

- Only the non-zero weights are uploaded, and only when `MorphWeights.Version` changes, so meshes
  with many targets cost one pass over their active targets per vertex.
- The deformed output belongs to the `Skinning` when the mesh is also skinned, otherwise to the
  `Morphing`. `MeshDrawInfo.DeformedVertexBuffer` returns the buffer the draws read.
- Morphed meshes are never frustum culled (`MeshDrawInfo.IsCullable` is `false`).
- A `Morphing` must not be shared between mesh entities; share the `MorphWeights` instead (as the
  primitives of a glTF mesh do). `Morphing` owns GPU buffers and must be disposed by its owner.
- `Morphing.MorphPosition` applies the uploaded weights on the CPU; picking uses it so pick positions
  match the morphed (and skinned) triangle.

### Adding a Billboard Component

```csharp
var billboardDrawInfo = new BillboardDrawInfo
{
    BillboardGeometry = new BillboardGeometry(),
    Color = new Color4(1f, 1f, 1f, 1f),
    RenderSpace = RenderSpace.World,
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
    renderSpace: RenderSpace.Screen
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

### Visualizing Skeletons

`SkeletonPostEffect` draws one line segment per joint, from the joint's parent to the joint, for every
skinned mesh entity that carries a `SkeletonPostEffect.SkeletonOverlay` component. Attach the overlay
to the skinned mesh entity with your ECS world's component-add API.

```csharp
var skeletonEffect = new SkeletonPostEffect
{
    Style = BoneStyle.Octahedral, // BoneStyle.Line, BoneStyle.Arrow (default), or BoneStyle.Octahedral
    HeadSize = 0.15f,             // Fraction of bone length in [0, 1]; defaults to 0.2
    UseDepthTest = false          // Default: bones are drawn on top of the scene
};

// Mark the skinned mesh entity for visualization with a custom color
// (SkeletonOverlay.Default is a yellow overlay):
var overlay = new SkeletonPostEffect.SkeletonOverlay(new Color4(0f, 1f, 0f, 1f));
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

### Configuring Sky Atmosphere

Requires `SkyAtmosphereNode` in the pipeline (`EngineBuilder.WithSkyAtmosphere()`).

```csharp
var sky = renderContext.SkyAtmosphere;
sky.Enabled = true;
sky.KmPerUnit = 0.001f;              // one world unit = one meter (default)
sky.SunFromDirectionalLight = true;  // sun follows directional light 0 (default)
sky.SunIntensityFromDirectionalLight = true; // sky brightness follows light 0 (default)
sky.DirectionalLightIntensityScale = 25f;     // light intensity 0.8 -> sun intensity 20
sky.SunColorFromDirectionalLight = true;     // sky tint follows light 0's colour (default)
sky.GroundColor = new Vector3(0.3f, 0.28f, 0.25f); // ground tint below the horizon
sky.AerialPerspectiveScale = 50f;    // exaggerate the haze for a small scene
// Optional: draw the environment cubemap behind the atmosphere as a star field.
sky.ShowEnvironmentMapAsSpace = true;
```

Custom PBR material types can haze their output by returning `applyAtmosphere(color)` from
their `OutputColorImplementation`.

## Architecture Notes

- **Entity Component System (ECS)**: The rendering engine uses an ECS architecture to manage entities and their components, allowing for flexible and efficient scene management.
- **Render Graph**: The render graph organizes rendering tasks into nodes, ensuring that resources are used efficiently and that tasks are executed in the correct order.
- **Reverse-Z**: The engine uses a reverse-Z depth buffer to improve precision in depth testing.
- **Post-Processing**: Post-processing effects are modular and can be added or removed easily, allowing for customizable rendering pipelines.

HelixToolkit.Nex.Rendering is designed to be both powerful and flexible, providing developers with the tools they need to create high-performance 3D applications.
