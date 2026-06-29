# HelixToolkit.Nex.Scene

The `HelixToolkit.Nex.Scene` package manages and manipulates scene-graph nodes within a 3D environment. It builds on the `HelixToolkit.Nex.ECS` framework to provide hierarchical transformations, node management, and scene sorting in a data-oriented, performant way.

## Overview

`HelixToolkit.Nex.Scene` is responsible for:
- Managing scene-graph nodes backed by ECS entities and components.
- Handling hierarchical transformations and parent-child relationships.
- Flattening and sorting nodes and updating world transforms.
- Deferred scene construction off the world thread via `SceneCommandBuffer`.

A `Node` wraps an ECS `Entity` and attaches the components needed for scene management (`NodeInfo`,
`Transform`, `WorldTransform`, `Parent`, and optionally `NodeName`, `Children`, `Renderable`). The
package depends on `HelixToolkit.Nex.ECS` for entity/component storage and `HelixToolkit.Nex.Maths`
for matrix math.

## Key Types

| Type                   | Description                                                                                                                    |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `Node`                 | A scene-graph node wrapping an `Entity`; manages transform and parent/child relationships.                                     |
| `NodeInfo`             | Component storing a node's hierarchy level, entity id, and enabled state. Implements `ISortable<NodeInfo>` (sorts by level).   |
| `NodeName`             | Optional component storing a node's display name.                                                                              |
| `Transform`            | Component holding local scale/rotation/translation plus a change `Timestamp`.                                                  |
| `WorldTransform`       | Component holding a node's computed world matrix (`Matrix4x4`).                                                                |
| `Parent`               | Component referencing a node's parent entity.                                                                                  |
| `Children`             | Component holding the list of child nodes.                                                                                     |
| `Renderable`           | Component marking a node for rendering, with a render mask and internal GPU indexing fields.                                   |
| `SceneSorting`         | Static/extension methods for flattening node trees and updating transforms (`Flatten`, `UpdateTransforms`, `SortSceneNodes`).  |
| `SceneCommandBuffer`   | Records deferred node creation, hierarchy, and properties off the world thread, then materializes real `Node`s during `Flush`. |
| `DeferredNode`         | Handle returned by `SceneCommandBuffer.RecordCreateNode()` that resolves to a real `Node` during flush.                        |
| `TypedDeferredNode<T>` | Typed handle for recording creation of a custom `Node` subtype `T`.                                                            |
| `SceneFlushResult`     | Result of `SceneCommandBuffer.Flush`, reporting success or the failing command index and description.                          |

## Usage Examples

### Creating and Managing Nodes

```csharp
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.ECS;

// Worlds are created through the ECS factory, not 'new'.
using var world = World.CreateWorld();

// Create a root node and a child.
var rootNode = new Node(world, "Root");
var childNode = new Node(world, "Child");
rootNode.AddChild(childNode);

// Sort by hierarchy level, then update world transforms.
world.SortSceneNodes();
world.UpdateTransforms();
```

### Accessing and Modifying Node Properties

```csharp
Console.WriteLine($"Root Node Name: {rootNode.Name}");
Console.WriteLine($"Child Node Level: {childNode.Level}");

childNode.Name = "Updated Child";

// Transform is exposed by ref, so it can be mutated in place.
childNode.Transform.Translation = new Vector3(1, 0, 0);
childNode.NotifyTransformChanged();
```

### Flattening and Updating Transforms

```csharp
// Flatten the tree (depth-first) into a sorted list, optionally filtered.
var sortedNodes = new List<Node>();
rootNode.Flatten(condition: null, sortedNodes);

// Update world transforms across the flattened, level-ordered list.
sortedNodes.UpdateTransforms();
```

### Controlling Renderable State

```csharp
childNode.IsRenderable = true;          // adds the Renderable component
bool isRenderable = childNode.IsRenderable;
```

## Scene Command Buffer

`SceneCommandBuffer` lets you build a scene graph **without touching any `World`** during recording,
so the work can run on a background thread. Recorded commands are materialized into real `Node`
objects during `Flush`, which must run on the world's owning thread. Recording is single-writer:
concurrent recording calls are rejected, leaving the buffer's state unchanged.

```csharp
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.ECS;

// 1. Record off the world thread — no World is referenced here.
var buffer = new SceneCommandBuffer();

var root = buffer.RecordCreateNode("Root");
var child = buffer.RecordCreateNode("Child");
buffer.RecordAddChild(root, child);
buffer.RecordLocalTransform(child, new Transform { Translation = new Vector3(1, 0, 0) });
buffer.RecordRenderable(child);

// 2. Flush on the world's owning thread to materialize real Nodes.
SceneFlushResult result = buffer.Flush(world);
if (result.Success)
{
    // Resolve a deferred handle to the materialized Node.
    if (buffer.MaterializedNodes.TryGetValue(child, out var childNode))
    {
        Console.WriteLine($"Materialized: {childNode.Name}");
    }
}
else
{
    Console.WriteLine(
        $"Flush failed at command {result.FailedCommandIndex}: {result.Message} ({result.Code})");
}
```

To create a custom `Node` subtype, record it with a factory:

```csharp
var handle = buffer.RecordCreateNode(world => new MyCustomNode(world));
buffer.Flush(world);
if (buffer.TryGetMaterializedNode(handle, out MyCustomNode? node) == ResultCode.Ok)
{
    // use node
}
```

## Architecture Notes

- **ECS-backed**: Each `Node` is an `Entity` with scene components; transforms and hierarchy live in ECS component storage.
- **Per-world node registry**: Nodes are tracked in a per-world `ConcurrentDictionary` keyed by entity id; the registry is dropped wholesale when the world is disposed.
- **Single-threaded per world**: A world is accessed by one thread at a time. Use `SceneCommandBuffer` to build nodes off-thread and apply them back on the world thread.
- **Level-ordered transform updates**: `NodeInfo` sorts by hierarchy level so parents are processed before children when world transforms are recomputed.
- **Dependencies**: `HelixToolkit.Nex.ECS` (entities/components) and `HelixToolkit.Nex.Maths` (matrix math).
