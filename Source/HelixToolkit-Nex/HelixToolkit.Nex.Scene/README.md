# HelixToolkit.Nex.Scene

The `HelixToolkit.Nex.Scene` package provides a robust scene graph system for managing hierarchical 3D objects in the HelixToolkit.Nex engine. It is built on top of the Entity Component System (ECS) architecture and enables efficient scene traversal, transformation updates, and hierarchical relationships between objects. This package is designed to handle complex 3D scenes while maintaining high performance and scalability.

---

## Overview

The `HelixToolkit.Nex.Scene` package is responsible for managing the logical structure of a 3D scene. It provides a hierarchical node-based system where each node represents an entity in the ECS. Nodes can have parent-child relationships, enabling the creation of complex scene graphs. The package also includes utilities for sorting and updating scene nodes, ensuring efficient rendering and transformation updates.

Key features include:

- **Node-based Scene Graph**: Manage hierarchical relationships between 3D objects.
- **ECS Integration**: Built on the Arch ECS library for efficient entity management.
- **Transform Management**: Handle local and world transformations with support for hierarchical updates.
- **Scene Sorting**: Sort nodes based on their hierarchy levels for optimized processing.
- **Thread-Safe World Registries**: Efficiently manage nodes across multiple ECS worlds.

---

## Key Types

| Type                  | Description                                                                 |
|-----------------------|-----------------------------------------------------------------------------|
| `Node`               | Represents a single node in the scene graph. Manages parent-child relationships and transformations. |
| `NodeInfo`           | Stores metadata about a node, such as its level in the hierarchy and enabled state. |
| `NodeName`           | Optional component for storing a human-readable name for a node.            |
| `Transform`          | Represents the local transformation of a node, including translation, rotation, and scale. |
| `WorldTransform`     | Represents the world transformation of a node, derived from its local transform and parent's world transform. |
| `Parent`             | Represents the parent entity of a node in the scene graph.                  |
| `Children`           | Represents the child nodes of a node in the scene graph.                    |
| `SceneSorting`       | Provides static methods for sorting and updating scene nodes.               |

---

## Usage Examples

### Creating a Scene Graph

```csharp
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Scene;
using System.Numerics;

// Create a new ECS world
var world = new World();

// Create a root node
var rootNode = new Node(world, "RootNode");

// Create child nodes
var childNode1 = new Node(world, "ChildNode1");
var childNode2 = new Node(world, "ChildNode2");

// Set parent-child relationships
rootNode.AddChild(childNode1);
rootNode.AddChild(childNode2);

// Update transformations
rootNode.Transform.Translation = new Vector3(0, 0, 0);
childNode1.Transform.Translation = new Vector3(1, 0, 0);
childNode2.Transform.Translation = new Vector3(0, 1, 0);

// Sort and update transforms
world.SortSceneNodes();
world.UpdateTransforms();
```

### Traversing and Flattening the Scene Graph

```csharp
using HelixToolkit.Nex.Scene;
using System.Collections.Generic;

// Flatten the scene graph into a sorted list
var sortedNodes = new List<Node>();
rootNode.Flatten(null, sortedNodes);

// Iterate through the sorted nodes
foreach (var node in sortedNodes)
{
    Console.WriteLine($"Node: {node.Name}, Level: {node.Level}, Enabled: {node.Enabled}");
}
```

### Enabling and Disabling Nodes

```csharp
using HelixToolkit.Nex.Scene;

// Disable a node and its children
childNode1.Enabled = false;

// Enable a node and its children
childNode1.Enabled = true;
```

---

## Architecture Notes

The `HelixToolkit.Nex.Scene` package is built on the following architectural principles:

- **Entity Component System (ECS)**: The package leverages the Arch ECS library for efficient entity and component management. Each `Node` is backed by an ECS entity, and its properties are stored as components.
- **Hierarchical Scene Graph**: Nodes are organized in a tree structure, with parent-child relationships managed through ECS components (`Parent` and `Children`).
- **Transform Propagation**: Local and world transformations are managed using the `Transform` and `WorldTransform` components. Changes to a node's transform automatically propagate to its children.
- **Sorting and Optimization**: The `SceneSorting` class provides methods for sorting nodes based on their hierarchy levels and updating their transformations in an efficient order.
- **Thread-Safe World Registries**: The package uses a two-level dictionary structure to manage nodes across multiple ECS worlds, ensuring thread safety and performance.

Dependencies:
- `HelixToolkit.Nex.ECS`: Provides the underlying ECS framework.
- `HelixToolkit.Nex.Maths`: Supplies mathematical utilities, including matrix and vector operations.

This package is a core component of the HelixToolkit.Nex engine, providing the foundation for building and managing complex 3D scenes.
