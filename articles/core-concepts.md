# Core Concepts

Learn about the core concepts and architecture of Helix Toolkit NEX.

## Architecture Overview

Helix Toolkit NEX is built with a layered architecture:

```
???????????????????????????????????????
?   Application Layer      ?
?   (Your Application Code)     ?
???????????????????????????????????????
?   HelixToolkit.Nex.Rendering  ?
?   (High-level rendering)?
???????????????????????????????????????
?   HelixToolkit.Nex.Scene            ?
?   (Scene graph management)          ?
???????????????????????????????????????
?   HelixToolkit.Nex.Graphics     ?
?   (Platform-independent API) ?
???????????????????????????????????????
?   HelixToolkit.Nex.Graphics.Vulkan  ?
?   (Vulkan implementation)           ?
???????????????????????????????????????
```

## Key Components

### Graphics Context (IContext)

The graphics context is the main entry point for all GPU operations:

- Resource creation (buffers, textures, pipelines)
- Command buffer management
- Queue submission and synchronization

### Command Buffers (ICommandBuffer)

Command buffers record GPU commands:

- Rendering passes
- Compute dispatches
- Resource copies
- State binding

### Resources

Resources represent GPU objects:

- **Buffers**: Vertex, index, uniform, and storage buffers
- **Textures**: 2D, 3D, and cube textures with mipmaps
- **Pipelines**: Graphics and compute pipelines
- **Samplers**: Texture sampling configuration

### Handles

The toolkit uses a handle-based system for resource management:

```csharp
public struct Handle<T>
{
    public uint Index { get; }
    public uint Gen { get; }  // Generation for ABA problem prevention
}
```

Benefits:
- Type-safe resource references
- Prevents dangling references through generational indices
- Lightweight (8 bytes per handle)

## Memory Management

The toolkit follows these principles:

1. **Explicit Resource Lifetime**: Resources are explicitly created and destroyed
2. **Reference Counting**: Resource wrappers use reference counting for automatic cleanup
3. **Handle Validation**: Handles include generation numbers to detect use-after-free

## Threading Model

- **Single-threaded submission**: Command buffers should be submitted from a single thread
- **Multi-threaded recording**: Command buffers can be recorded in parallel (future feature)
- **Thread-safe resource creation**: Context methods are thread-safe

## Synchronization

The toolkit uses Vulkan's synchronization2 model:

- Pipeline barriers for resource transitions
- Submit handles for CPU-GPU synchronization
- Automatic dependency tracking

## Best Practices

1. **Reuse command buffers**: Acquire and reuse command buffers each frame
2. **Batch operations**: Group draw calls and dispatches
3. **Minimize state changes**: Sort by pipeline and resources
4. **Use staging buffers**: For frequently updated data
5. **Enable validation**: Always test with validation layers

## Next Steps

- Review the [API Reference](../api/index.md) for detailed information
- Explore the samples in the repository
- Read about specific subsystems (rendering, scene management, etc.)
