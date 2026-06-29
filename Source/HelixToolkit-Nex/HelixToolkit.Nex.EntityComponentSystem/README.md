# HelixToolkit.Nex.ECS

HelixToolkit.Nex.ECS is the Entity Component System (ECS) framework used by the HelixToolkit.Nex 3D graphics engine. It provides a data-oriented way to manage entities and their components, with isolated worlds, a built-in event bus, and a thread-safe deferred command buffer for off-thread recording.

## Overview

HelixToolkit.Nex.ECS supports:
- **World management**: Create and dispose isolated `World` instances, each with its own entity space.
- **Entity management**: Create, enable/disable, validate, and dispose entities.
- **Component management**: Attach, read (by `ref`), update, and remove strongly-typed components.
- **Tag components**: Attach data-less marker components via `Entity.Tag<T>()` (no storage is allocated).
- **Filtered collections**: Build live `EntityCollection`s from component-presence rules.
- **Event system**: A per-world event bus for component-change and user-defined events.
- **Component sorting**: Sort component storage in place via the `ISortable<T>` interface.
- **Deferred command buffers**: Record entity create / set / remove operations off the world thread and apply them in order during a flush.

## Key Types

| Type               | Description                                                                                                                      |
| ------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| `World`            | An isolated collection of entities and components. Created with `World.CreateWorld()`.                                           |
| `Entity`           | A lightweight value-type handle to an entity. Provides `Set`/`Get`/`Has`/`Remove`/`Tag` component operations.                    |
| `Components<T>`    | A view over the contiguous component storage of type `T` within a world.                                                         |
| `EntityCollection` | A live collection of entities matching a rule; updates as component/enable state changes.                                        |
| `RuleBuilder`      | Fluent builder (`Has<T>`, `NotHas<T>`, `EnabledOnly`, `Build`) for creating an `EntityCollection`.                               |
| `ISortable<T>`     | Interface implemented by components that define custom in-place sort ordering.                                                   |
| `ComponentTypeId`  | Unique identifier for a component type (supports up to 128 distinct types per process).                                          |
| `Subscription`     | Handle returned by event registration; `Dispose()` to unsubscribe.                                                               |
| `CommandBuffer`    | Records deferred entity create / set / remove commands without touching a world, then applies them during `Flush`.               |
| `DeferredEntity`   | Handle returned by `CommandBuffer.RecordCreateEntity()` that resolves to a real `Entity` during flush.                           |
| `FlushResult`      | Result of `CommandBuffer.Flush`, reporting success or the index and description of the failing command.                          |
| `ResultCode`       | Shared `HelixToolkit.Nex` result enum returned by component operations (`Ok`, `InvalidState`, `NotFound`, `WorldNotValid`, ...). |

> Note: `ComponentManager<T>` and `TagManager<T>` are internal storage managers. Consumers interact with components only through `World` and `Entity`.

## Usage Examples

### Creating a World and Entities

```csharp
using HelixToolkit.Nex.ECS;

using var world = World.CreateWorld();
var entity = world.CreateEntity();
```

### Adding and Retrieving Components

```csharp
struct Position { public float X, Y, Z; }

var position = new Position { X = 1.0f, Y = 2.0f, Z = 3.0f };
entity.Set(ref position);

if (entity.Has<Position>())
{
    ref var pos = ref entity.Get<Position>(); // returns by ref; check entity.Valid first
    Console.WriteLine($"Position: {pos.X}, {pos.Y}, {pos.Z}");
}
```

### Tag (Marker) Components

```csharp
struct Selected { } // empty struct: no data storage is allocated

entity.Tag<Selected>();
bool isSelected = entity.Has<Selected>();
entity.Remove<Selected>();
```

### Using Entity Collections

```csharp
using var collection = world.CreateCollection()
    .Has<Position>()
    .NotHas<Selected>()
    .EnabledOnly()
    .Build();

foreach (var e in collection)
{
    Console.WriteLine($"Entity {e.Id} is in the collection.");
}
```

### Handling Events

The per-world event bus raises a public `ComponentChangedEvent<T>` whenever a component of type `T`
is added, changed, or removed. You can also `Send` and `Register` your own message types.

```csharp
using HelixToolkit.Nex.ECS.Events;

// React to Position being added/changed/removed on any entity in the world.
var subscription = world.Register<ComponentChangedEvent<Position>>((w, evt) =>
{
    Console.WriteLine($"Entity {evt.EntityId}: Position {evt.Operation}");
});

// Custom user-defined events work too.
readonly record struct ScoreChanged(int EntityId, int Score);
world.Send(new ScoreChanged(entity.Id, 42));

// Unsubscribe when done.
subscription.Dispose();
```

## Command Buffer

`CommandBuffer` records deferred entity operations **without referencing any `World`**, which makes it
safe to build on a background thread. The recorded commands are applied in order during `Flush`, which
must run on the world's owning thread. Each `RecordCreateEntity()` returns a `DeferredEntity` handle
that resolves to a real `Entity` only at flush time.

Recording contract:
- Recording is single-writer: record into a given buffer from one thread at a time.
- `RecordSet` / `RecordRemove` return `ResultCode.InvalidState` (and record nothing) if the
  `DeferredEntity` was not produced by that buffer.
- A successful `Flush` empties the buffer, so a subsequent flush is a no-op.

### Recording and Flushing

```csharp
using HelixToolkit.Nex.ECS;

struct Position { public float X, Y, Z; }
struct Velocity { public float X, Y, Z; }

// 1. Record off the world thread — no World is touched here.
var buffer = new CommandBuffer();

var a = buffer.RecordCreateEntity();
buffer.RecordSet(a, new Position { X = 1, Y = 2, Z = 3 });
buffer.RecordSet(a, new Velocity { X = 0, Y = 0, Z = 1 });

var b = buffer.RecordCreateEntity();
buffer.RecordSet(b, new Position { X = 5, Y = 5, Z = 5 });

Console.WriteLine($"Pending commands: {buffer.PendingCount}");

// 2. Flush on the world's owning thread.
FlushResult result = buffer.Flush(world);
if (!result.Success)
{
    Console.WriteLine(
        $"Flush failed at command {result.FailedCommandIndex}: {result.Message} ({result.Code})");
}
```

### Removing a Component via the Buffer

```csharp
var e = buffer.RecordCreateEntity();
buffer.RecordSet(e, new Position { X = 0, Y = 0, Z = 0 });
buffer.RecordRemove<Position>(e); // applied in recorded order during Flush

buffer.Flush(world);
```

## Architecture Notes

- **Data-oriented design**: Components are stored contiguously per type and per world; behavior lives in systems that iterate `Components<T>` or `EntityCollection`s.
- **World isolation**: Each `World` has its own entity space and event bus. World ids and generations are recycled safely (generation 0 is reserved as "invalid").
- **Single-threaded per world**: A given world is expected to be accessed from one thread at a time. The `CommandBuffer` is the supported mechanism for building entity data off-thread and applying it back on the world thread.
- **Component type identification**: `ComponentTypeId` uniquely identifies each component type, supporting up to 128 distinct types.
- **Event-driven**: Component add/change/remove operations raise `ComponentChangedEvent<T>`; `RuleBuilder`/`EntityCollection` use these to stay in sync.
- **Tag components**: Data-less marker structs added via `Entity.Tag<T>()` allocate no component storage.
