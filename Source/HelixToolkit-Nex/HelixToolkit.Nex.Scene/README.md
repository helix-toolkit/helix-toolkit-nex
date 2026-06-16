```markdown
# HelixToolkit.Nex.Scene

The `HelixToolkit.Nex.Scene` package is a core component of the HelixToolkit-Nex 3D graphics engine, designed to manage and manipulate scene graph nodes within a 3D environment. It provides a robust framework for handling hierarchical transformations, node management, and scene sorting, leveraging the Entity Component System (ECS) architecture for efficient and scalable scene management.

## Overview

The `HelixToolkit.Nex.Scene` package is responsible for:
- Managing scene graph nodes using an ECS-based architecture.
- Handling hierarchical transformations and parent-child relationships.
- Providing utilities for scene sorting and transformation updates.
- Integrating with the HelixToolkit-Nex engine's ECS and rendering systems.

This package fits into the HelixToolkit-Nex engine by providing the foundational scene graph management capabilities necessary for rendering complex 3D scenes. It utilizes the Arch ECS library to efficiently manage entities and components, ensuring that scene updates and transformations are handled in a performant manner.

## Key Types

| Type              | Description                                                                 |
|-------------------|-----------------------------------------------------------------------------|
| `Node`            | Represents a scene graph node, managing transformations and parent-child relationships. |
| `NodeInfo`        | Stores metadata about a node, including its level in the hierarchy and enabled state. |
| `NodeName`        | Optional component for storing a display name for a node.                   |
| `Transform`       | Manages local and world transformations for a node, includes a `Timestamp` property for tracking updates. |
| `WorldTransform`  | Represents a node's world transformation matrix, constructed with a reference to a `Matrix4x4`. |
| `Parent`          | Component that stores a reference to a node's parent entity.                |
| `Children`        | Manages a list of child nodes for a given node.                             |
| `SceneSorting`    | Provides static methods for flattening and sorting scene nodes.             |
| `Renderable`      | Represents an object that can be rendered by a graphics or UI system, with properties for render layers and GPU indexing. |

## Usage Examples

### Creating and Managing Nodes

```csharp
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.ECS;

// Create a new world
var world = new World();

// Create a root node
var rootNode = new Node(world, "Root");

// Create a child node and add it to the root
var childNode = new Node(world, "Child");
rootNode.AddChild(childNode);

// Update transformations
world.SortSceneNodes();
world.UpdateTransforms();
```

### Accessing and Modifying Node Properties

```csharp
// Access node properties
Console.WriteLine($"Root Node Name: {rootNode.Name}");
Console.WriteLine($"Child Node Level: {childNode.Level}");

// Modify node properties
childNode.Name = "Updated Child";
childNode.Transform.Translation = new Vector3(1, 0, 0);
```

### Scene Sorting and Transformation Updates

```csharp
// Flatten the scene graph into a sorted list
var sortedNodes = new List<Node>();
rootNode.Flatten(null, sortedNodes);

// Update world transformations for all nodes
sortedNodes.UpdateTransforms();
```

### Controlling Renderable State

```csharp
// Set a node as renderable
childNode.IsRenderable = true;

// Check if a node is renderable
bool isRenderable = childNode.IsRenderable;
Console.WriteLine($"Is Child Node Renderable: {isRenderable}");
```

## Architecture Notes

- **Entity Component System (ECS):** The package uses the Arch ECS library to manage entities and components, ensuring efficient updates and scalability.
- **Design Patterns:** The package employs a registry pattern for managing node lookups, optimizing for single-threaded access per world.
- **Dependencies:** This package depends on `HelixToolkit.Nex.ECS` for ECS functionality and `HelixToolkit.Nex.Maths` for mathematical operations.
- **Thread Safety:** Node operations are designed to be thread-safe across different worlds, with each world being accessed by a single thread at a time.

## Recent Changes

- **Node Class Enhancements:**
  - The `IsRenderable` property now checks if the `Renderable` component is already present before adding or removing it, preventing unnecessary operations.
  - The `NotifyComponentChanged<T>()` method now uses the `[DynamicallyAccessedMembers]` attribute to specify the required member types for `T`.
  - Introduced `FindNode(Entity entity)` method to find a node associated with a given entity.

- **Transform Updates:**
  - The `UpdateWorldTransform` method in `Transform` now multiplies the local transformation matrix with the parent matrix in the correct order, ensuring accurate world transformations.

- **Renderable Structure:**
  - Added `DrawType` and `DrawVariants` fields to the `Renderable` struct for enhanced rendering control.

- **Registry Improvements:**
  - Replaced the inner dictionary of `_worldRegistries` with `ConcurrentDictionary` to improve thread safety and simplify the code by removing explicit locks.
  - Added `FindNode(Entity entity)` method to easily retrieve a node from an entity.

The `HelixToolkit.Nex.Scene` package is an essential part of the HelixToolkit-Nex engine, providing the necessary infrastructure for managing complex 3D scenes efficiently.
```
