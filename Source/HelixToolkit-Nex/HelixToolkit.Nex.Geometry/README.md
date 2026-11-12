# HelixToolkit.Nex.Geometries

The `HelixToolkit.Nex.Geometries` package is a core component of the HelixToolkit-Nex 3D graphics engine, designed to handle geometric data structures and operations. It provides functionality for managing, processing, and rendering geometric data, including vertices, indices, and vertex properties. This package is essential for creating and manipulating 3D models within the HelixToolkit-Nex engine.

## Overview

The `HelixToolkit.Nex.Geometries` package is responsible for:
- Managing geometric data through the `Geometry` class, which includes vertices, indices, and vertex properties.
- Providing utilities for normal calculation, tangent generation, and buffer management.
- Supporting serialization and deserialization of geometric data.
- Implementing octree structures for efficient spatial queries and hit testing.
- Integrating with the HelixToolkit-Nex ECS architecture for dynamic and static geometry management.

## Key Types

| Type                       | Description                                                                                         |
| -------------------------- | --------------------------------------------------------------------------------------------------- |
| `Geometry`                 | Represents a 3D geometry with vertices, indices, and vertex properties.                             |
| `VertexProperties`         | Struct containing normal, texture coordinates, and tangent information for a vertex.                |
| `GeometryBufferType`       | Enum defining the types of buffers used in a geometry, such as vertex, index, and color buffers.    |
| `GeometryManager`          | Manages a pool of geometry resources with automatic ID assignment and lifecycle management.         |
| `HitTestContext`           | Provides context for performing hit tests, including ray and screen point information.              |
| `HitTestResult`            | Represents the result of a hit test, including distance, hit point, and normal at the hit location. |
| `IOctreeBasic`             | Interface for basic octree operations, used for spatial queries and hit testing.                    |
| `StaticMeshGeometryOctree` | Static octree implementation for mesh geometries, optimizing spatial queries and hit tests.         |
| `GeometryJsonConverter`    | JSON converter for serializing and deserializing `Geometry` objects.                                |
| `TangentGenerator`         | Static class for generating tangent vectors for geometries that lack them.                          |
| `NormalGenerator`          | Static class for generating flat (per-face) normals; unwelds indexed triangle lists.                |
| `MorphTarget`              | Morph target (blend shape) holding optional per-vertex position, normal and tangent deltas.         |

`Geometry.JointInfluences` holds optional per-vertex bone influences (`GpuJointInfluence`: four joint
indices and normalized weights) used for GPU skinning. Their GPU buffer is `JointInfluenceBuffer`
(`GeometryBufferType.Skin`); `IsSkinned` reports whether any are present. Joint influences are not
serialized by `GeometryJsonConverter` yet.

`Geometry.MorphTargets` holds optional morph targets (blend shapes). Each `MorphTarget` has
`PositionDeltas`, `NormalDeltas` and `TangentDeltas`, each either empty or one delta per vertex. All
targets are packed target-major into `MorphDeltaBuffer` (`GeometryBufferType.Morph`, one
`GpuMorphDelta` per target and vertex); `HasMorphTargets` reports whether any are present. The
weights live outside the geometry (`MorphWeights` in the Scene package), so one geometry can be
drawn with different weights. `NormalGenerator` expands the morph target deltas when it unwelds the
vertices. Morph targets are not serialized by `GeometryJsonConverter` yet.

`Geometry.HasFlatNormals` and `Geometry.HasGeneratedTangents` record whether the normals or tangents
were generated from the vertex data rather than authored. `HasFlatNormals` is set by
`NormalGenerator.ComputeFlatNormals`. When it is `true`, the deformation pass recomputes the normals
from the morphed triangles, because morph targets carry no normal deltas for generated normals.
`HasGeneratedTangents` tells the deformation pass to keep the tangents consistent with the morphed
surface.

## Usage Examples

### Creating and Managing Geometry

```csharp
var vertices = new FastList<Vector4>
{
    new Vector4(0, 0, 0, 1),
    new Vector4(1, 0, 0, 1),
    new Vector4(0, 1, 0, 1)
};

var indices = new FastList<uint> { 0, 1, 2 };

var geometry = new Geometry(vertices, indices, Topology.Triangle);
geometry.UpdateNormals();
TangentGenerator.ComputeTangents(geometry);
```

### Generating Flat Normals

```csharp
var geometry = new Geometry(vertices, indices, Topology.Triangle);

// Unwelds indexed triangles: vertices, vertex properties, vertex colors and joint
// influences are duplicated per triangle corner and the indices become sequential.
NormalGenerator.ComputeFlatNormals(geometry);
```

### Performing a Hit Test

```csharp
var renderMatrices = ... // Obtain IRenderMatrices instance
var ray = new Ray(new Vector3(0, 0, -1), Vector3.UnitZ);
var hitPoint = new Vector2(100, 100);

var hitTestContext = new HitTestContext(renderMatrices, ray, hitPoint);
var hits = new List<HitTestResult>();

if (geometry.HitTest(hitTestContext, entity, geometry, Matrix.Identity, ref hits))
{
    foreach (var hit in hits)
    {
        Console.WriteLine($"Hit at distance: {hit.Distance}");
    }
}
```

### Serializing Geometry

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new GeometryJsonConverter() }
};

string json = JsonSerializer.Serialize(geometry, options);
var deserializedGeometry = JsonSerializer.Deserialize<Geometry>(json, options);
```

## Architecture Notes

- **Design Patterns**: The package uses the Entity Component System (ECS) architecture for managing dynamic and static geometries, built on the custom `HelixToolkit.Nex.ECS` framework.
- **Dependencies**: It relies on other HelixToolkit-Nex packages such as `HelixToolkit.Nex.Graphics` for rendering and `HelixToolkit.Nex.Maths` for mathematical operations.
- **Octree Implementation**: The package includes several octree implementations for efficient spatial queries, such as `StaticMeshGeometryOctree` and `StaticPointGeometryOctree`.
- **Serialization**: Custom JSON converters are provided for `Geometry` and `VertexProperties` to facilitate serialization and deserialization.

## Recent Changes

- **Target Framework**: Updated to target .NET 9.0.
- **VertexProperties Struct**: 
  - The `Tangent` field has been updated to a `Vector4` to include handedness information, aligning with the glTF specification.
  - Introduced `DefaultTangent` for initializing tangents with a default right-handed bitangent.
- **Geometry Class**: 
  - Updated buffer management logic to use `HasAllFlags` for checking buffer types.
  - Buffer debug names have been updated for clarity.
  - Removed redundant event publishing logic from property change handlers.
  - Added joint influence support: `JointInfluences`, `JointInfluenceBuffer`, `IsSkinned`, and `GeometryBufferType.Skin`.
- **GeometryManager API**: 
  - Introduced `UploadMeshInfoDynamic` method for uploading mesh information to the GPU.
  - Added `LastIndex` property to track the last object index in the pool.
- **TangentGenerator Class**: 
  - New static class for generating tangent vectors for geometries using the Lengyel algorithm.
- **NormalGenerator Class**: 
  - New static class with `ComputeFlatNormals` for generating per-face normals. Indexed geometry is unwelded first, so its vertex count may change.
- **Serialization**: 
  - Updated `VertexPropsJsonConverter` to handle `Vector4` tangents.
- **Platform Configurations**: Added new build configurations for `LinuxDebug` and `LinuxRelease` to support cross-platform development.
- **Morph Targets**: 
  - Added `Geometry.MorphTargets`, `MorphDeltaBuffer`, `HasMorphTargets`, `GeometryBufferType.Morph`, and the `MorphTarget` class.
- **Flat Normal and Tangent Flags**: 
  - Added `Geometry.HasFlatNormals` and `Geometry.HasGeneratedTangents`.
