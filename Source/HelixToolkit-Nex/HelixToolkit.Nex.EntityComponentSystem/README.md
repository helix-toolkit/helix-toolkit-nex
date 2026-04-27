```markdown
# HelixToolkit.Nex.ECS

HelixToolkit.Nex.ECS is a robust Entity Component System (ECS) framework designed for the HelixToolkit.Nex 3D graphics engine. It provides a flexible and efficient way to manage entities and their components, leveraging the Arch ECS library to facilitate high-performance operations in complex 3D environments.

## Overview

HelixToolkit.Nex.ECS is integral to the HelixToolkit.Nex engine, providing a structured approach to managing entities and their associated components. It supports:
- **Entity Management**: Create, manage, and dispose of entities within a world.
- **Component Management**: Attach, retrieve, and manipulate components associated with entities.
- **Event System**: A robust event bus for handling entity and component lifecycle events.
- **World Management**: Manage multiple worlds with isolated entity spaces.
- **Component Sorting**: Sort components efficiently using custom sorting logic.

## Key Types

| Type | Description |
|------|-------------|
| `Entity` | Represents an individual entity in the ECS framework. |
| `ComponentManager<T>` | Manages components of type `T` for entities within a world. |
| `World` | Represents a collection of entities and components, providing isolation between different sets of entities. |
| `EntityCollection` | Provides a dynamic collection of entities filtered by specified criteria. |
| `RuleBuilder` | Constructs rules for filtering entities based on component presence and state. |
| `ResultCode` | Enum representing the result of ECS operations, such as `Ok`, `Invalid`, or `NotFound`. |
| `ISortable<T>` | Interface for defining custom sorting logic for components. |

## Usage Examples

### Creating a World and Entities

```csharp
var world = World.CreateWorld();
var entity = world.CreateEntity();
```

### Adding and Retrieving Components

```csharp
struct Position { public float X, Y, Z; }

var position = new Position { X = 1.0f, Y = 2.0f, Z = 3.0f };
entity.Set(ref position);

if (entity.Has<Position>())
{
    ref var pos = ref entity.Get<Position>();
    Console.WriteLine($"Position: {pos.X}, {pos.Y}, {pos.Z}");
}
```

### Using Entity Collections

```csharp
var collection = world.CreateCollection()
    .Has<Position>()
    .EnabledOnly()
    .Build();

foreach (var e in collection)
{
    Console.WriteLine($"Entity {e.Id} is in the collection.");
}
```

### Handling Events

```csharp
entity.Register<EntityEnableEvent>((worldId, evt) =>
{
    Console.WriteLine($"Entity {evt.EntityId} enabled state changed to {evt.Enabled}");
});
```

## Architecture Notes

- **Entity Component System (ECS)**: Built on the Arch ECS library, providing a data-oriented design that separates data (components) from behavior (systems).
- **Event-Driven Architecture**: Utilizes an event bus (`ECSEventBus`) for decoupled communication between entities and systems.
- **Component Sorting**: Supports custom sorting of components via the `ISortable<T>` interface, allowing for optimized data access patterns.
- **World Isolation**: Each `World` instance provides a separate namespace for entities, ensuring no interference between different worlds.
- **Component Type Identification**: Uses `ComponentTypeId` for unique identification of component types, supporting up to 128 unique types.

## Recent Changes

- **New Configurations**: Added support for `LinuxDebug` and `LinuxRelease` configurations in the project file.
- **Component Sorting**: Enhanced component sorting using Shell Sort for improved performance.
- **Entity Management**: Improved entity lifecycle management with additional event handling for entity disposal.

HelixToolkit.Nex.ECS is designed to be flexible and efficient, making it suitable for a wide range of 3D applications within the HelixToolkit.Nex engine.
```
