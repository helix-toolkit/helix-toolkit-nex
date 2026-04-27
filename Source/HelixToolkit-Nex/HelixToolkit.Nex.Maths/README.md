```markdown
# HelixToolkit.Nex.Maths

The `HelixToolkit.Nex.Maths` package provides a comprehensive set of mathematical utilities and structures designed to support 3D graphics operations within the HelixToolkit-Nex engine. This package includes implementations for vectors, matrices, colors, bounding volumes, and collision detection, among other mathematical constructs essential for 3D rendering and physics calculations.

## Overview

The `HelixToolkit.Nex.Maths` package is integral to the HelixToolkit-Nex engine, providing the mathematical backbone required for rendering and physics simulations. It includes:
- Support for various color formats and operations.
- Vector and matrix operations optimized for 3D graphics.
- Bounding volumes like boxes, spheres, and frustums for collision detection and spatial queries.
- Utility functions for geometric calculations and transformations.

## Key Types

| Type                  | Description                                                                 |
|-----------------------|-----------------------------------------------------------------------------|
| `Color`               | Represents a 32-bit color in RGBA format.                                   |
| `Color3`              | Represents a color in RGB format.                                           |
| `Color4`              | Represents a color in RGBA format with floating-point precision.            |
| `BoundingBox`         | Represents an axis-aligned bounding box in 3D space.                        |
| `BoundingSphere`      | Represents a bounding sphere in 3D space.                                   |
| `BoundingFrustum`     | Defines a frustum for frustum culling and intersection tests.               |
| `Vector3`             | Represents a vector with three components.                                  |
| `Vector4`             | Represents a vector with four components.                                   |
| `Matrix`              | Represents a 4x4 matrix used for transformations.                           |
| `Ray`                 | Represents a ray in 3D space for intersection tests.                        |
| `Plane`               | Represents a plane in 3D space.                                             |
| `Collision`           | Provides static methods for collision detection and geometric queries.      |
| `Bool4`               | Represents a four-dimensional vector of boolean values.                     |
| `Bool32Bit`           | Represents a boolean value with a size of 32 bits.                          |
| `AngleSingle`         | Represents a unit-independent angle using a single-precision floating-point.|

## Usage Examples

### Creating and Using Colors

```csharp
using HelixToolkit.Nex.Maths;

// Create a color using RGBA values
Color color = new Color(255, 0, 0, 255); // Red color

// Convert to Color4 for floating-point operations
Color4 color4 = color.ToColor4();

// Adjust color brightness
Color4 brighterColor = Color4Helper.ChangeIntensity(color4, 1.2f);
```

### Working with Bounding Volumes

```csharp
using HelixToolkit.Nex.Maths;

// Create a bounding box
BoundingBox box = new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

// Check if a point is inside the bounding box
Vector3 point = new Vector3(0.5f, 0.5f, 0.5f);
bool isInside = box.Contains(point) == ContainmentType.Contains;

// Create a bounding sphere
BoundingSphere sphere = new BoundingSphere(new Vector3(0, 0, 0), 1.0f);

// Check for intersection between box and sphere
bool intersects = box.Intersects(ref sphere);
```

### Collision Detection

```csharp
using HelixToolkit.Nex.Maths;

// Define a ray
Ray ray = new Ray(new Vector3(0, 0, -5), Vector3.UnitZ);

// Define a plane
Plane plane = new Plane(Vector3.UnitY, 0);

// Check for intersection
if (Collision.RayIntersectsPlane(ref ray, ref plane, out float distance))
{
    Vector3 intersectionPoint = ray.Position + ray.Direction * distance;
    Console.WriteLine($"Intersection at: {intersectionPoint}");
}
```

### Using Bool4 and Bool32Bit

```csharp
using HelixToolkit.Nex.Maths;

// Create a Bool4 vector
Bool4 boolVector = new Bool4(true, false, true, false);

// Access components
bool xValue = boolVector.X;

// Create a Bool32Bit
Bool32Bit bool32 = new Bool32Bit(true);

// Convert to and from bool
bool normalBool = bool32;
Bool32Bit convertedBack = normalBool;
```

### Working with Angles

```csharp
using HelixToolkit.Nex.Maths;

// Create an angle in degrees
AngleSingle angle = new AngleSingle(90, AngleType.Degree);

// Convert to radians
float radians = angle.Radians;

// Wrap the angle
angle.Wrap();
```

## Architecture Notes

- **Design Patterns**: The package extensively uses value types (structs) for performance-critical operations, minimizing heap allocations.
- **Dependencies**: This package is a foundational component of the HelixToolkit-Nex engine and is used by other packages for rendering and physics calculations.
- **Matrix and Vector Operations**: Optimized for 3D graphics, supporting both row-major and column-major formats as needed.
- **Bounding Volumes**: Essential for spatial queries and collision detection, supporting efficient intersection tests and containment checks.
```
