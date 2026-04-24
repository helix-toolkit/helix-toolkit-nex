---
inclusion: always
---

# HelixToolkit Nex — Project Context

## Overview

HelixToolkit Nex is a C# 3D visualization engine targeting .NET 8.0. It uses Vulkan 1.3 as its graphics backend, a bindless render pipeline for shader implementations, PBR materials as the default mesh material, and an Entity Component System (ECS) for scene graph and data management.

The solution lives under `Source/HelixToolkit-Nex/HelixToolkit.Nex.sln`.

## Project / Module Map

| Project                                  | Purpose                                                                                                                                                        |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `HelixTookit.Nex`                        | Foundation utilities: DI container, event bus, object pool, ring buffer, logging, tracing, FastList, IdHelper                                                  |
| `HelixTookit.Nex.Core`                   | Shared core types used across the engine                                                                                                                       |
| `HelixToolkit.Nex.Maths`                 | Math library (vectors, matrices, bounding volumes)                                                                                                             |
| `HelixToolkit.Nex.Geometry`              | Mesh geometry types and data structures                                                                                                                        |
| `HelixToolkit.Nex.Geometries.Builders`   | Procedural geometry builders (box, sphere, etc.)                                                                                                               |
| `HelixToolkit.Nex.Graphics`              | Backend-agnostic GPU abstraction: `IContext`, `ICommandBuffer`, resource handles, descriptors                                                                  |
| `HelixToolkit.Nex.Graphics.Vulkan`       | Vulkan 1.3 backend implementation (Vortice.Vulkan bindings, VMA allocator, descriptor management, swapchain)                                                   |
| `HelixToolkit.Nex.Graphics.Mock`         | Mock graphics backend for testing                                                                                                                              |
| `HelixToolkit.Nex.Shaders`               | GLSL shader compilation, `ShaderBuilder` with preprocessor, include resolution, and header management                                                          |
| `HelixToolkit.Nex.Material`              | PBR material system: `MaterialTypeRegistry`, `PBRMaterialShaderBuilder`, specialization constants, uber shaders                                                |
| `HelixToolkit.Nex.Rendering`             | Render graph (`RenderGraph`), render nodes, render passes, ping-pong buffers, post-effects (SMAA, FXAA, Bloom, Tone Mapping, Border Highlight, Wireframe, OIT) |
| `HelixToolkit.Nex.EntityComponentSystem` | ECS: `World`, `Entity`, component storage, entity collections, `ECSEventBus`                                                                                   |
| `HelixToolkit.Nex.Scene`                 | Scene-level abstractions built on top of ECS                                                                                                                   |
| `HelixToolkit.Nex.Engine`                | High-level engine orchestration                                                                                                                                |
| `HelixToolkit.Nex.Repository`            | Asset / resource repository                                                                                                                                    |
| `HelixToolkit.Nex.ImGui`                 | ImGui integration for debug UI                                                                                                                                 |
| `HelixToolkit.Nex.CodeGen`               | Source generators                                                                                                                                              |
| `Samples/`                               | Sample applications (HelloTriangle, ForwardPlus, MeshCulling, ImGui, Point Clouds, etc.)                                                                       |

Note: the folder `HelixTookit.Nex` (with the typo) is the actual on-disk name for the foundation project.

## Key Architectural Patterns

### Graphics Abstraction Layer
- `IContext` — main interface for GPU resource creation, command submission, and swapchain management.
- `ICommandBuffer` — records draw calls, compute dispatches, pipeline binds, and resource transitions.
- Resource handles (`BufferHandle`, `TextureHandle`, `RenderPipelineHandle`, etc.) are reference-counted.

### Vulkan Backend
- Vulkan 1.3 with `VK_KHR_DYNAMIC_RENDERING` (no traditional render passes).
- Bindless rendering via large descriptor sets with dynamic texture/sampler management.
- Push descriptor extension for efficient per-draw descriptor updates.
- VMA (Vulkan Memory Allocator) for GPU memory management.
- Async upload via dedicated transfer queue.

### Render Graph
- `RenderGraph` compiles a DAG of `GraphNode` entries with topological sorting.
- Supports ping-pong buffer groups for multi-pass effects without runtime buffer swaps.
- Render nodes (`RenderNode` base class) plug into the graph.

### PBR Material System
- `MaterialTypeRegistry` registers material types with unique IDs and specialization constants.
- `PBRMaterialShaderBuilder` generates GLSL uber shaders or single-type shaders.
- Template-based fragment shader generation with customizable output functions.
- Integrates with Forward+ tile-based light culling.

### Entity Component System
- `World` manages entities with generation-tracked IDs (max 255 worlds).
- Components stored in typed arrays with change notification.
- `ECSEventBus` for inter-system communication.
- Entity collections with rule-based filtering.

### Point Cloud Rendering
- Compute-based frustum culling via `PointCullNode` (expands surviving points to screen-space billboard quads).
- `PointRenderNode` draws culled points with depth testing, outputting to color + entity ID textures.
- `PointCloudComponent` ECS component stores geometry, color, size, material ID, fixed-size flag, and texture/sampler indices.
- `PointCloudData` collects all `PointCloudComponent` entities per frame, grouped by material type.
- Shaders in `Shaders/Point/`: `PointStructs.glsl`, `csPointExpand.glsl`, `vsPoint.glsl`, `psPointTemplate.glsl`.
- Bindless buffer references for point positions, colors, and draw data.
- GPU picking: compute shader stamps entity ID + point index per quad.

### Point Material System
- `PointMaterialRegistry` — static registry mapping material names to unique `MaterialTypeId` values and GLSL `outputColor()` implementations.
- `PointMaterialManager` — creates render pipelines per registered material type (uber shader with specialization constants).
- Built-in default material: circle SDF with optional bindless texture sampling (ID = 0).
- Custom materials registered via `PointMaterialRegistry.Register(name, glslCode)`.
- Fragment shader template (`psPointTemplate.glsl`) injects material-specific code at compile time.

### Rendering Pipeline Features
- Forward+ (tile-based GPU light culling)
- GPU frustum culling (mesh and instanced)
- Weighted Blended Order-Independent Transparency (WBOIT)
- Post-effects: SMAA, FXAA, Bloom, Tone Mapping, Border Highlighting, Wireframe
- GPU object-level picking
- Point cloud rendering (see above)

## Build & Test

- Target: .NET 8.0
- Key NuGet deps: `Vortice.Vulkan`, `VulkanMemoryAllocator`, `ImGui.NET`
- Test framework: xUnit
- Test projects: `*.Tests` siblings for Graphics, ECS, Rendering, Shaders, Geometry, Material, Maths, Scene, CodeGen
- Solution file: `Source/HelixToolkit-Nex/HelixToolkit.Nex.sln`

## Coding Conventions

- Modern C# idioms: records, spans, `unsafe` where needed for interop
- Namespace convention: `HelixToolkit.Nex.<Module>` (e.g. `HelixToolkit.Nex.ECS`, `HelixToolkit.Nex.Rendering`)
- Reference-counted resource lifetime management
- Builder pattern for shader and material configuration (`PBRMaterialShaderBuilder`, `ShaderBuilder`)
- Static factory methods for world/entity creation (`World.CreateWorld()`)
