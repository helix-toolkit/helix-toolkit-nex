# HelixToolkit.Nex.Scene

The `HelixToolkit.Nex.Scene` package manages and manipulates scene-graph nodes within a 3D environment. It builds on the `HelixToolkit.Nex.ECS` framework to provide hierarchical transformations, node management, and scene sorting in a data-oriented, performant way.

## Overview

`HelixToolkit.Nex.Scene` is responsible for:
- Managing scene-graph nodes backed by ECS entities and components.
- Handling hierarchical transformations and parent-child relationships.
- Flattening and sorting nodes and updating world transforms.
- Deferred scene construction off the world thread via `SceneCommandBuffer`.
- Keyframe animation of node transforms (`AnimationClip`, `AnimationPlayer`).

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
| `AnimationSampler`     | Immutable keyframes (times + `Vector4` values) with `Linear` (slerp for rotations), `Step` or `CubicSpline` interpolation.      |
| `AnimationChannel`     | Animates the `Translation`, `Rotation` or `Scale` of a target `Node`, or up to four `MorphWeights` (`AnimationPath.Weights`).  |
| `MorphWeights`         | Morph target weights of a mesh, shared by its primitives; `Version` changes on every weight change.                           |
| `AnimationClip`        | Named set of channels with a common time range; `Apply(time)` poses the targets.                                               |
| `AnimationPlayer`      | Plays a clip (`Play`/`Pause`/`Stop`, `Speed`, `Loop`, `Time`); restores the targets' rest pose on `Stop` or clip change.        |

## New Features

### Deferred Action Command

The `SceneCommandBuffer` now supports recording arbitrary actions to be executed during the flush on the world's owning thread. This is achieved through the `RecordDeferredAction` method.

#### `SceneCommandBuffer.RecordDeferredAction`

Records a deferred action to run on the world's owning thread during flush, in recorded order.

```csharp
public ResultCode RecordDeferredAction(Action<World> action, string description);
```

- **Parameters:**
  - `action`: The delegate to invoke against the target `World` during flush. All required data must be captured inside the delegate.
  - `description`: A human-readable description surfaced in flush failure messages.

- **Returns:** `ResultCode.Ok` on success; `ResultCode.InvalidState` if `action` is `null` or a concurrent recording operation is in progress.

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

### Recording Deferred Actions

```csharp
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.ECS;

// 1. Record off the world thread — no World is referenced here.
var buffer = new SceneCommandBuffer();

buffer.RecordDeferredAction(world => {
    // Custom action to be executed during flush
    Console.WriteLine("Deferred action executed.");
}, "Custom deferred action");

// 2. Flush on the world's owning thread to materialize real Nodes and execute actions.
SceneFlushResult result = buffer.Flush(world);
if (result.Success)
{
    Console.WriteLine("Flush succeeded.");
}
else
{
    Console.WriteLine(
        $"Flush failed at command {result.FailedCommandIndex}: {result.Message} ({result.Code})");
}
```

## Animation

Animations write node `Transform`s and morph target weights. An `AnimationClip` is a set of
`AnimationChannel`s, each animating one transform property of a target node, or morph target weights,
with an `AnimationSampler`. glTF imports produce clips already bound to the imported nodes
(`ImportResult.Animations`); clips can also be built by hand:

```csharp
var sampler = new AnimationSampler(
    [0f, 1f],                                   // keyframe times in seconds
    [Vector4.Zero, new Vector4(0, 2, 0, 0)],    // translations use XYZ, rotations store a quaternion as XYZW
    AnimationInterpolation.Linear);
var clip = new AnimationClip("Bounce", [new AnimationChannel(node, AnimationPath.Translation, sampler)]);

var player = new AnimationPlayer(clip) { Loop = true, Speed = 1f };
player.Play();

// Per frame, on the world's owning thread, before rendering:
player.Update(deltaTime);
```

`AnimationPlayer.Update` advances the time while playing and applies the clip; a paused player only
re-applies after `Time` or `Clip` changes. Applying a clip notifies `Transform` changes, so the engine
updates world transforms (and the joints of skinned meshes) on the next frame. The player remembers
the targets' transforms before it first poses them and restores them on `Stop` or when `Clip`
changes. Channels whose target is `null` or disposed are skipped.

Morph target weights are animated by `AnimationPath.Weights` channels. Each channel writes the four
components of its sampler to the `MorphWeights` at `WeightOffset` and the following indices (a glTF
weights channel with N targets becomes ceil(N / 4) channels). The player restores the animated weights
on `Stop` or clip change as well.

```csharp
var weights = new MorphWeights(2);                            // one weight per morph target
var sampler = new AnimationSampler(
    [0f, 1f],
    [new Vector4(0, 0, 0, 0), new Vector4(1, 0.5f, 0, 0)],    // weights 0 and 1 per keyframe
    AnimationInterpolation.Linear
);
var clip = new AnimationClip("Smile", [new AnimationChannel(weights, 0, sampler)]);
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

## Compatibility

- **Target Framework**: Updated to .NET 9.0.
