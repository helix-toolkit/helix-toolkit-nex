```markdown
# HelixToolkit.Nex.Geometries.Builders

The `HelixToolkit.Nex.Geometries.Builders` package provides a set of utilities for constructing and manipulating 3D geometrical shapes and meshes. It is a part of the HelixToolkit-Nex engine, which is designed for high-performance 3D graphics rendering using the Vulkan API. This package specifically focuses on building complex geometries such as meshes, polygons, and other 3D shapes, facilitating operations like triangulation, contour slicing, and mesh simplification.

## Overview

The `HelixToolkit.Nex.Geometries.Builders` package is responsible for:
- Constructing 3D mesh geometries from basic shapes.
- Performing operations like triangulation and contour slicing.
- Simplifying meshes using advanced algorithms.
- Supporting various geometric transformations and operations.

This package fits into the HelixToolkit-Nex engine by providing the foundational tools necessary for creating and manipulating 3D objects, which are then rendered using the Vulkan API.

## Key Types

| Type Name               | Description                                                                 |
|-------------------------|-----------------------------------------------------------------------------|
| `BoxFaces`              | Enum representing the faces of a box.                                       |
| `ContourHelper`         | Class for calculating contour slices through 3D meshes.                     |
| `CuttingEarsTriangulator` | Static class implementing the cutting ears triangulation algorithm.        |
| `MeshBuilder`           | Class for constructing `MeshGeometry3D` objects with various shapes.        |
| `MeshFaces`             | Enum representing different mesh face configurations.                       |
| `MeshGeometry3D`        | Class representing a 3D mesh with positions, normals, and texture coordinates. |
| `MeshGeometryHelper`    | Static class providing helper methods for mesh geometries.                  |
| `MeshSimplification`    | Class implementing mesh simplification using the Fast-Quadric-Mesh-Simplification algorithm. |
| `Polygon`               | Class representing a 2D polygon.                                            |
| `Polygon3D`             | Class representing a 3D polygon.                                            |
| `SweepLinePolygonTriangulator` | Static class for triangulating polygons using the sweep line algorithm. |

## Usage Examples

### Creating a Box Mesh

```csharp
var meshBuilder = new MeshBuilder();
meshBuilder.AddBox(new Vector3(0, 0, 0), 1, 1, 1);
MeshGeometry3D boxMesh = meshBuilder.ToMesh();
```

### Triangulating a Polygon

```csharp
var polygonPoints = new List<Vector2>
{
    new Vector2(0, 0),
    new Vector2(1, 0),
    new Vector2(1, 1),
    new Vector2(0, 1)
};
var indices = SweepLinePolygonTriangulator.Triangulate(polygonPoints);
```

### Simplifying a Mesh

```csharp
var meshSimplification = new MeshSimplification(originalMesh);
MeshGeometry3D simplifiedMesh = meshSimplification.Simplify(targetCount: 100);
```

## Architecture Notes

- **Design Patterns**: The package employs several design patterns, including the Builder pattern for constructing complex mesh geometries and the Strategy pattern for different triangulation algorithms.
- **Dependencies**: This package depends on other HelixToolkit-Nex packages, such as `HelixToolkit.Nex.Maths` for mathematical operations and vector manipulations.
- **Performance**: The package is optimized for performance with features like caching and SIMD optimizations for vector operations.
- **Integration**: It integrates seamlessly with the HelixToolkit-Nex engine's ECS and Render Graph systems, allowing for efficient rendering and manipulation of 3D objects.

This package is crucial for developers working with the HelixToolkit-Nex engine, providing the tools necessary to create and manipulate 3D geometries efficiently.

## Recent Changes

### MeshGeometry3D

- **Tangent Calculation Update**: The tangent vector in `MeshGeometry3D` is now represented as a `Vector4`, with the W component set to `1.0f`. This change ensures compatibility with shaders that expect a 4D tangent vector.

```csharp
var tangentXYZ = Tangents is not null ? Tangents[i] : Vector3.Zero;
var tangent = new Vector4(tangentXYZ, 1.0f);
```

This change affects how tangents are stored and should be considered when interfacing with shaders or other systems that utilize tangent data.

### Project Configuration

- **Package Icon**: Added `icon.png` as the package icon.
- **Build Configurations**: Added new build configurations `LinuxDebug` and `LinuxRelease` to support cross-platform development.
```
