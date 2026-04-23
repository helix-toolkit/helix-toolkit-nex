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

| Type          | Description                                                                 |
|---------------|-----------------------------------------------------------------------------|
| `Node`        | Represents a scene graph node, managing transformations and parent-child relationships. |
| `NodeInfo`    | Stores metadata about a node, including its level in the hierarchy and enabled state. |
| `NodeName`    | Optional component for storing a display name for a node.                   |
| `Transform`   | Manages local and world transformations for a node.                         |
| `WorldTransform` | Represents a node's world transformation matrix.                         |
| `Parent`      | Component that stores a reference to a node's parent entity.                |
| `Children`    | Manages a list of child nodes for a given node.                             |
| `SceneSorting`| Provides static methods for flattening and sorting scene nodes.             |

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

## Architecture Notes

- **Entity Component System (ECS):** The package uses the Arch ECS library to manage entities and components, ensuring efficient updates and scalability.
- **Design Patterns:** The package employs a registry pattern for managing node lookups, optimizing for single-threaded access per world.
- **Dependencies:** This package depends on `HelixToolkit.Nex.ECS` for ECS functionality and `HelixToolkit.Nex.Maths` for mathematical operations.
- **Thread Safety:** Node operations are designed to be thread-safe across different worlds, with each world being accessed by a single thread at a time.

The `HelixToolkit.Nex.Scene` package is an essential part of the HelixToolkit-Nex engine, providing the necessary infrastructure for managing complex 3D scenes efficiently.
```
