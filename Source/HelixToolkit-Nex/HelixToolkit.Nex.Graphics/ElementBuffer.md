# ElementBuffer Usage Guide

## Overview

`ElementBuffer<T>` is a generic GPU buffer management class designed to simplify working with Storage Buffers (SSBOs) that contain collections of structured data. It automatically handles buffer resizing, memory allocation strategies, and data uploads.

## Key Features

- **Automatic Resizing**: Grows the buffer automatically when capacity is exceeded
- **Two Allocation Modes**: 
  - **Dynamic** (frequent updates): Uses HOST_VISIBLE memory with map/unmap for direct CPU writes
  - **Static** (infrequent updates): Uses device-local memory for best GPU performance
- **Type-Safe**: Generic implementation ensures compile-time type safety
- **Easy to Use**: Simple API for uploading FastList<T> data to GPU

## Basic Usage

### Creating a Dynamic Buffer (Frequent Updates)

```csharp
// Create a dynamic buffer for data that changes every frame
// Uses HOST_VISIBLE memory with map/unmap for direct CPU access
var dynamicBuffer = new ElementBuffer<Matrix4x4>(context, capacity: 1000, isDynamic: true);

// Upload data (resizes automatically if needed)
// Data is written directly to mapped memory - no staging overhead!
FastList<Matrix4x4> matrices = GetInstanceTransforms();
dynamicBuffer.Upload(matrices);

// Use the buffer in rendering
cmdBuffer.PushConstants(new MyConstants 
{
    InstanceBufferAddress = dynamicBuffer.Buffer.GpuAddress
});
```

### Creating a Static Buffer (Infrequent Updates)

```csharp
// Create a static buffer for data that rarely changes
var staticBuffer = new ElementBuffer<PBRProperties>(context, capacity: 100, isDynamic: false);

// Upload material data
FastList<PBRProperties> materials = LoadMaterials();
staticBuffer.Upload(materials);

// The buffer uses device-local memory for optimal GPU performance
```

## Upload Mechanisms

### Dynamic Buffers: Map/Unmap

Dynamic buffers use **mapped memory** for direct CPU writes:

1. Buffer is created with `StorageType.HostVisible`
2. Memory is persistently mapped during buffer lifetime
3. Uploads copy data directly to mapped memory (`NativeHelper.MemoryCopy`)
4. Memory is flushed after write if not coherent (`FlushMappedMemory`)

**Benefits:**
- ✅ Zero-copy upload (no staging buffer)
- ✅ Fastest for per-frame updates
- ✅ Minimal CPU overhead
- ⚠️ Slightly slower GPU reads vs device-local

```csharp
// Example: Per-frame instance updates
for (int frame = 0; frame < 1000; frame++)
{
    var instances = GenerateFrameInstances();
    dynamicBuffer.Upload(instances);  // Direct memory write via map!
    
    RenderFrame();
}
```

### Static Buffers: Staging Upload

Static buffers use **staging mechanism** for optimal GPU performance:

1. Buffer is created with `StorageType.Device` (device-local memory)
2. Uploads go through `Context.Upload()` staging mechanism
3. Staging buffer transfers data to device-local memory
4. Best GPU read performance

**Benefits:**
- ✅ Optimal GPU read performance
- ✅ Best for infrequent updates
- ⚠️ Upload has staging overhead

```csharp
// Example: One-time material upload
var materials = LoadMaterialLibrary();
staticBuffer.Upload(materials);  // Via staging, but only done once

// Materials can be used for thousands of frames without re-uploading
```

## Resizing Behavior

### Dynamic Buffers (IsDynamic = true)

When data size exceeds capacity:
1. Creates a new buffer with **1.5x the required capacity** (growth room)
2. Uses **HOST_VISIBLE** storage with persistent mapping
3. Old buffer is disposed automatically

**Example:**
```csharp
var buffer = new ElementBuffer<float>(context, capacity: 100, isDynamic: true);

// First upload: 100 elements -> no resize
buffer.Upload(data100);

// Second upload: 150 elements -> resizes to 225 capacity (150 * 1.5)
buffer.Upload(data150);

// Third upload: 200 elements -> no resize (fits in 225)
buffer.Upload(data200);
```

### Static Buffers (IsDynamic = false)

When data size exceeds capacity:
1. Creates a new buffer with **exactly the required capacity**
2. Uses **Device-local** storage for best GPU performance
3. Old buffer is disposed automatically

**Example:**
```csharp
var buffer = new ElementBuffer<Vertex>(context, capacity: 100, isDynamic: false);

// First upload: 100 vertices -> no resize
buffer.Upload(vertices100);

// Second upload: 150 vertices -> resizes to exactly 150 capacity
buffer.Upload(vertices150);
```

## Performance Characteristics

### Dynamic Buffer (Map/Unmap)

```
Upload Path: CPU → Mapped Memory → GPU
Memory Type: HOST_VISIBLE (+ HOST_COHERENT if available)
Upload Time: ~0.1-1 μs per MB (direct write)
GPU Read:    Slightly slower than device-local
Best For:    Per-frame or multi-per-frame updates
```

**Real-world timings (1000 Matrix4x4 = 64KB):**
- Dynamic upload: ~0.05 ms (map/unmap)
- Static upload: ~0.15 ms (staging)

### Static Buffer (Staging)

```
Upload Path: CPU → Staging Buffer → GPU Transfer → Device Memory
Memory Type: Device-local (VRAM)
Upload Time: ~1-10 μs per MB (staging overhead)
GPU Read:    Fastest possible
Best For:    One-time or infrequent updates
```

## Advanced Usage

### Manual Capacity Management

```csharp
var buffer = new ElementBuffer<MyStruct>(context, capacity: 0, isDynamic: true);

// Pre-allocate capacity if you know the size ahead of time
buffer.EnsureCapacity(10000);

// Now uploads up to 10000 elements won't trigger resize
buffer.Upload(myData);
```

### Integration with GPU Culling

```csharp
// Example: Instance culling with ElementBuffer
var instanceBuffer = new ElementBuffer<Matrix4x4>(context, capacity: 10000, isDynamic: true);
var visibleInstanceBuffer = new ElementBuffer<uint>(context, capacity: 10000, isDynamic: true);

// Upload all instance transforms (fast map/unmap)
instanceBuffer.Upload(allInstances);

// Run compute shader for GPU culling
cmdBuffer.BindComputePipeline(cullingPipeline);
cmdBuffer.PushConstants(new CullingConstants 
{
    InstanceBufferAddress = instanceBuffer.Buffer.GpuAddress,
    VisibleInstanceBufferAddress = visibleInstanceBuffer.Buffer.GpuAddress,
    InstanceCount = (uint)allInstances.Count
});
cmdBuffer.DispatchThreadGroups(groupCount, Dependencies.Empty);

// Render only visible instances
cmdBuffer.DrawIndexedIndirect(drawCommandBuffer, 0, 1, stride);
```

### Working with Large Datasets

```csharp
// Dynamic buffer with initial capacity
var buffer = new ElementBuffer<LightData>(context, capacity: 1000, isDynamic: true);

// Upload grows automatically
FastList<LightData> lights = new();
for (int i = 0; i < 5000; i++)
{
    lights.Add(CreateLight(i));
}

// Resizes from 1000 -> 7500 capacity (5000 * 1.5)
// All subsequent uploads use fast map/unmap!
buffer.Upload(lights);

// Buffer can be reused for smaller uploads without reallocation
lights.Resize(2000);
buffer.Upload(lights); // No resize, direct map/unmap
```

## Performance Considerations

### When to Use Dynamic Buffers

- ✅ Data changes every frame (e.g., instance transforms, skinning matrices)
- ✅ CPU needs to frequently write to the buffer
- ✅ Size varies frequently
- ✅ Update speed is critical (map/unmap is fastest)
- ⚠️ Slightly slower GPU reads compared to device-local memory

### When to Use Static Buffers

- ✅ Data rarely changes (e.g., static geometry, material properties)
- ✅ Best GPU read performance needed
- ✅ Known or stable size
- ✅ Upload performance not critical
- ⚠️ Resizing requires buffer recreation (more expensive)

### Memory Usage Tips

1. **Pre-allocate for known sizes**:
   ```csharp
   // Good: Avoid resizing
   var buffer = new ElementBuffer<T>(context, knownSize, isDynamic: true);
   
   // Less optimal: Multiple resizes
   var buffer = new ElementBuffer<T>(context, 1, isDynamic: true);
   ```

2. **Choose appropriate growth strategy**:
   - Dynamic buffers grow by 1.5x to reduce resize frequency
   - Static buffers use exact size to minimize memory waste

3. **Dispose when no longer needed**:
   ```csharp
   using var buffer = new ElementBuffer<T>(context, capacity, isDynamic);
   // Automatically disposed at end of scope
   ```

## Common Patterns

### Pattern 1: Frame-based Updates (Dynamic/Map)

```csharp
public class Renderer : IDisposable
{
    private readonly ElementBuffer<PerFrameData> _frameDataBuffer;
    
    public Renderer(IContext context)
    {
        // Use dynamic for per-frame updates
        _frameDataBuffer = new ElementBuffer<PerFrameData>(
            context, 
            capacity: 1, 
            isDynamic: true  // Uses map/unmap!
        );
    }
    
    public void Render(Camera camera)
    {
        var frameData = new FastList<PerFrameData> { CreateFrameData(camera) };
        _frameDataBuffer.Upload(frameData);  // Fast mapped write
        
        // Use buffer in shaders...
    }
    
    public void Dispose()
    {
        _frameDataBuffer?.Dispose();
    }
}
```

### Pattern 2: Multi-Material Scene (Mixed)

```csharp
public class Scene : IDisposable
{
    private readonly ElementBuffer<Material> _materialBuffer;
    private readonly ElementBuffer<DrawCommand> _drawCommandBuffer;
    
    public Scene(IContext context)
    {
        // Materials rarely change -> static (device-local)
        _materialBuffer = new ElementBuffer<Material>(
            context, 
            capacity: 100, 
            isDynamic: false  // Uses staging
        );
        
        // Draw commands vary per frame -> dynamic (map/unmap)
        _drawCommandBuffer = new ElementBuffer<DrawCommand>(
            context, 
            capacity: 1000, 
            isDynamic: true  // Uses map/unmap!
        );
    }
    
    public void UpdateMaterials(FastList<Material> materials)
    {
        _materialBuffer.Upload(materials);  // Staging (infrequent)
    }
    
    public void SubmitDraws(FastList<DrawCommand> commands)
    {
        _drawCommandBuffer.Upload(commands);  // Map/unmap (every frame)
    }
    
    public void Dispose()
    {
        _materialBuffer?.Dispose();
        _drawCommandBuffer?.Dispose();
    }
}
```

## Error Handling

The `Upload` method returns a `ResultCode` that should be checked:

```csharp
var buffer = new ElementBuffer<MyData>(context, capacity: 100, isDynamic: true);

var result = buffer.Upload(myDataList);
if (result.HasError())
{
    // Handle error
    logger.LogError($"Failed to upload data: {result}");
    return;
}

// Continue with rendering...
```

## Shader Access

Access the buffer in shaders using buffer device addresses (bindless):

```glsl
layout(push_constant) uniform PushConstants {
    uint64_t instanceBufferAddress;
} pc;

layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer InstanceBuffer {
    mat4 transforms[];
};

void main() {
    InstanceBuffer instances = InstanceBuffer(pc.instanceBufferAddress);
    mat4 transform = instances.transforms[gl_InstanceIndex];
    
    // Use transform...
}
```

## Limitations

1. **Maximum buffer size**: Limited by `IContext.GetMaxStorageBufferRange()`
2. **Element type**: Must be `unmanaged` (value types with no managed references)
3. **Alignment**: Elements should be 16-byte aligned for optimal performance
4. **Single-threaded**: Not thread-safe, use synchronization if accessed from multiple threads

## Migration from Manual Buffer Management

**Before (Manual):**
```csharp
private BufferResource _buffer = BufferResource.Null;
private uint _capacity = 0;

void Upload(FastList<T> data)
{
    if (data.Count > _capacity)
    {
        _buffer?.Dispose();
        context.CreateBuffer(..., out _buffer);
        _capacity = (uint)data.Count;
    }
    
    unsafe
    {
        using var pin = data.GetInternalArray().Pin();
        context.Upload(_buffer.Handle, 0, (nint)pin.Pointer, size);
    }
}
```

**After (ElementBuffer):**
```csharp
private readonly ElementBuffer<T> _buffer;

_buffer = new ElementBuffer<T>(context, capacity: 0, isDynamic: true);

void Upload(FastList<T> data)
{
    _buffer.Upload(data);  // That's it! Auto-resize + map/unmap
}
```

## Best Practices

1. ✅ Use `using` statements or implement `IDisposable` to ensure cleanup
2. ✅ Choose `isDynamic=true` for data updated multiple times per frame
3. ✅ Choose `isDynamic=false` for data updated less than once per second
4. ✅ Pre-allocate capacity if size is known to avoid resizes
5. ✅ Check `ResultCode` return values in production code
6. ✅ Use dynamic buffers for maximum upload performance (map/unmap)
7. ✅ Use static buffers for maximum GPU read performance (device-local)

## See Also

- [BufferDesc Documentation](BufferDesc.cs)
- [IContext.CreateBuffer API](Context.cs)
- [FastList<T> Documentation](../HelixToolkit.Nex/FastList.cs)
- [GPU Culling Example](../Samples/GraphicsAPI/InstancingMeshCulling/InstancingMeshCulling.cs)
