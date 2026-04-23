```markdown
# HelixToolkit.Nex.Scene

The `HelixToolkit.Nex.Scene` package provides a robust framework for managing hierarchical scene graphs in 3D applications built with the HelixToolkit.Nex engine. It leverages an Entity Component System (ECS) architecture to efficiently handle nodes, transforms, and parent-child relationships, enabling scalable and performant scene management.

---

## Overview

The `HelixToolkit.Nex.Scene` package is responsible for managing the hierarchical structure of 3D scenes. It provides tools for creating, organizing, and manipulating nodes within a scene graph. Each node represents an entity in the ECS system, and the package integrates tightly with the HelixToolkit.Nex engine's ECS-based architecture. Key features include:

- **Node Hierarchies**: Support for parent-child relationships and hierarchical transformations.
- **Transform Management**: Efficient local and world transform updates using ECS components.
- **Scene Sorting**: Flattening and sorting scene nodes for optimized rendering and traversal.
- **Thread-Safe Registries**: Per-world registries for node lookup without unnecessary synchronization overhead.

---

## Key Types

| Type                     | Description                                                                 |
|--------------------------|-----------------------------------------------------------------------------|
| `Node`                  | Represents a single node in the scene graph, with properties for hierarchy and transforms. |
| `NodeInfo`              | Stores metadata about a node, including its level in the hierarchy and enabled state. |
| `NodeName`              | Optional display name for a node, stored as a separate ECS component.       |
| `Transform`             | Manages local transformations (scale, rotation, translation) and computes world transforms. |
| `WorldTransform`        | Represents the computed world transform of a node.                         |
| `Parent`                | Stores the parent entity of a node.                                         |
| `Children`              | Manages child nodes of a parent node.                                       |
| `SceneSorting`          | Provides static methods for sorting and updating scene nodes.               |

---

## Usage Examples

### Creating a Scene Graph

```csharp
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.ECS;

// Create a world
var world = World.CreateWorld();

// Create root node
var rootNode = new Node(world, "Root");

// Create child nodes
var childNode1 = new Node(world, "Child1");
var childNode2 = new Node(world, "Child2");

// Add children to the root node
rootNode.AddChild(childNode1);
rootNode.AddChild(childNode2);

// Update transforms
world.SortSceneNodes();
world.UpdateTransforms();
```

### Accessing Node Properties

```csharp
// Set node properties
rootNode.Transform.Scale = new Vector3(2, 2, 2);
childNode1.Transform.Translation = new Vector3(1, 0, 0);
childNode2.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);

// Access parent and children
Console.WriteLine($"Root has {rootNode.ChildCount} children.");
Console.WriteLine($"Child1's parent: {childNode1.Parent?.Name}");
```

### Flattening and Sorting Nodes

```csharp
using HelixToolkit.Nex.Scene;

// Flatten the scene graph into a sorted list
var sortedNodes = new List<Node>();
rootNode.Flatten(null, sortedNodes);

// Iterate through sorted nodes
foreach (var node in sortedNodes)
{
    Console.WriteLine($"Node: {node.Name}, Level: {node.Level}");
}
```

---

## Architecture Notes

- **Entity Component System (ECS)**: The package relies on the ECS architecture provided by the HelixToolkit.Nex engine, using the Arch ECS library for efficient component management.
- **Per-World Node Registries**: Nodes are registered in per-world registries for fast lookup without unnecessary synchronization overhead. This design ensures thread safety while adhering to the ECS contract.
- **Scene Sorting**: Nodes are sorted by their hierarchical level using the `NodeInfo` component. This enables efficient traversal and transform updates.
- **Transform Propagation**: Transform updates propagate through the hierarchy, ensuring that child nodes inherit changes from their parents.
- **Dependencies**: The package integrates tightly with `HelixToolkit.Nex.ECS` for entity management and `HelixToolkit.Nex.Maths` for mathematical operations.

---

## Additional Resources

For more information about the HelixToolkit.Nex engine and its components, visit the [HelixToolkit.Nex Documentation](https://github.com/helix-toolkit/helix-toolkit-nex).
```
