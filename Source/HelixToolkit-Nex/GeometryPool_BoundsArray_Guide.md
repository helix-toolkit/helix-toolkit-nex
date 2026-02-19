# GeometryPool Bounds Array for GPU Frustum Culling

## The Simple Answer

**The `GeometryHandle` already has an `Index` property that gives you the exact array index!**

```csharp
var handle = geometryPool.Create(geometry);
uint arrayIndex = handle.Index;  // ✅ This is your array index for GPU upload
```

## Complete Example: Building Bounds Array for Frustum Culling

### 1. Direct Index Mapping (Recommended for Static Allocation)

```csharp
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Repository;

public class FrustumCullingManager
{
    private readonly GeometryPool _geometryPool;
    private readonly IContext _context;
    private BufferResource _boundsBuffer = BufferResource.Null;
    
    public FrustumCullingManager(IContext context, GeometryPool pool)
    {
        _context = context;
        _geometryPool = pool;
    }
    
    /// <summary>
    /// Builds a bounds array where index matches handle.Index
    /// </summary>
    public void UploadBoundsToGPU()
    {
        // Calculate the maximum index to determine array size
        var handles = _geometryPool.GetAllHandles().ToList();
        if (handles.Count == 0) return;
        
        // The array needs to accommodate the highest index
        uint maxIndex = handles.Max(h => h.Index);
        var boundsArray = new BoundingBox[maxIndex + 1];
        
        // Fill the array - index directly corresponds to handle.Index
        foreach (var handle in handles)
        {
            var geometry = _geometryPool.Get(handle);
            if (geometry != null)
            {
                // Update bounds if needed
                geometry.UpdateBounds();
                
                // Store at the index specified by the handle
                boundsArray[handle.Index] = geometry.BoundingBoxLocal;
            }
        }
        
        // Upload to GPU
        UploadToGPU(boundsArray);
    }
    
    private unsafe void UploadToGPU(BoundingBox[] bounds)
    {
        uint sizeInBytes = (uint)(bounds.Length * sizeof(BoundingBox));
        
        // Dispose old buffer
        _boundsBuffer.Dispose();
        
        // Create new GPU buffer
        fixed (BoundingBox* ptr = bounds)
        {
            var result = _context.CreateBuffer(
                new BufferDesc(
                    BufferUsageBits.Storage | BufferUsageBits.TransferDst,
                    StorageType.Device,
                    (nint)ptr,
                    sizeInBytes
                ),
                out _boundsBuffer,
                debugName: "GeometryBoundsBuffer"
            );
            
            if (result != ResultCode.Ok)
            {
                throw new Exception($"Failed to create bounds buffer: {result}");
            }
        }
    }
}
```

### 2. Dense Packing (For Memory Efficiency)

If you want a compact array without gaps (when geometries are destroyed):

```csharp
public class DenseGeometryBoundsManager
{
    private readonly GeometryPool _pool;
    private readonly Dictionary<uint, int> _handleIndexToDenseIndex = new();
    private readonly List<BoundingBox> _denseBounds = new();
    
    public DenseGeometryBoundsManager(GeometryPool pool)
    {
        _pool = pool;
    }
    
    /// <summary>
    /// Rebuilds a dense bounds array and mapping
    /// </summary>
    public void RebuildBoundsArray()
    {
        _handleIndexToDenseIndex.Clear();
        _denseBounds.Clear();
        
        int denseIndex = 0;
        foreach (var handle in _pool.GetAllHandles())
        {
            var geometry = _pool.Get(handle);
            if (geometry != null)
            {
                // Update geometry bounds
                geometry.UpdateBounds();
                
                // Map sparse handle index to dense array index
                _handleIndexToDenseIndex[handle.Index] = denseIndex;
                _denseBounds.Add(geometry.BoundingBoxLocal);
                denseIndex++;
            }
        }
    }
    
    /// <summary>
    /// Gets the GPU array index for a given geometry handle
    /// </summary>
    public int GetGPUIndex(GeometryHandle handle)
    {
        return _handleIndexToDenseIndex.TryGetValue(handle.Index, out int denseIndex) 
            ? denseIndex 
            : -1;
    }
    
    /// <summary>
    /// Gets the dense bounds array for GPU upload
    /// </summary>
    public BoundingBox[] GetBoundsArray()
    {
        return _denseBounds.ToArray();
    }
}
```

### 3. Incremental Update (For Dynamic Geometry)

If geometries change frequently:

```csharp
public class IncrementalBoundsUpdater
{
    private readonly GeometryPool _pool;
    private readonly IContext _context;
    private BufferResource _boundsBuffer = BufferResource.Null;
    private BoundingBox[] _boundsArray = Array.Empty<BoundingBox>();
    
    public void Initialize()
    {
        var maxIndex = _pool.GetAllHandles().Max(h => h.Index);
        _boundsArray = new BoundingBox[maxIndex + 1];
        
        // Initial full upload
        RebuildAndUpload();
    }
    
    /// <summary>
    /// Updates a single geometry's bounds on GPU
    /// </summary>
    public void UpdateGeometryBounds(GeometryHandle handle)
    {
        var geometry = _pool.Get(handle);
        if (geometry == null) return;
        
        geometry.UpdateBounds();
        var newBounds = geometry.BoundingBoxLocal;
        
        // Update local array
        _boundsArray[handle.Index] = newBounds;
        
        // Upload just this one entry to GPU
        unsafe
        {
            uint offset = handle.Index * (uint)sizeof(BoundingBox);
            fixed (BoundingBox* ptr = &newBounds)
            {
                _context.Upload(_boundsBuffer, offset, (nint)ptr, (uint)sizeof(BoundingBox));
            }
        }
    }
    
    private void RebuildAndUpload()
    {
        foreach (var handle in _pool.GetAllHandles())
        {
            var geometry = _pool.Get(handle);
            if (geometry != null)
            {
                geometry.UpdateBounds();
                _boundsArray[handle.Index] = geometry.BoundingBoxLocal;
            }
        }
        
        // Upload entire array
        // ... (similar to UploadToGPU method above)
    }
}
```

## GLSL Shader Side

On the GPU frustum culling shader:

```glsl
// Bounds buffer layout
layout(std430, binding = 0) buffer BoundsBuffer {
    BoundingBox bounds[];  // Index matches GeometryHandle.Index
};

// Indirect draw command buffer
struct IndirectDrawCommand {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int vertexOffset;
    uint firstInstance;
};

layout(std430, binding = 1) buffer DrawCommandBuffer {
    IndirectDrawCommand commands[];
};

// Frustum culling shader
layout(local_size_x = 64) in;

void main() {
    uint geometryIndex = gl_GlobalInvocationID.x;
    if (geometryIndex >= bounds.length()) return;
    
    // Get bounds at index that matches the GeometryHandle.Index
    BoundingBox bbox = bounds[geometryIndex];
    
    // Perform frustum test
    bool visible = frustumTest(bbox, viewProj);
    
    // Update instance count in indirect draw command
    if (visible) {
        commands[geometryIndex].instanceCount = 1;
    } else {
        commands[geometryIndex].instanceCount = 0;
    }
}
```

## CPU-Side Rendering Loop

```csharp
public void Render(CommandBuffer cmd)
{
    // Update bounds for any changed geometries
    foreach (var handle in changedGeometries)
    {
        boundsUpdater.UpdateGeometryBounds(handle);
    }
    
    // Dispatch frustum culling compute shader
    cmd.BindComputePipeline(frustumCullingPipeline);
    cmd.BindBuffer(0, boundsBuffer);
    cmd.BindBuffer(1, indirectDrawBuffer);
    cmd.Dispatch(geometryCount / 64 + 1, 1, 1);
    
    // Barrier
    cmd.PipelineBarrier(/* ... */);
    
    // Execute indirect draws
    cmd.BindRenderPipeline(renderPipeline);
    foreach (var handle in _pool.GetAllHandles())
    {
        // The handle.Index tells you which indirect draw command to use
        cmd.DrawIndexedIndirect(
            indirectDrawBuffer, 
            handle.Index * sizeof(IndirectDrawCommand),  // ✅ Direct mapping!
            1
        );
    }
}
```

## Key Points

✅ **`handle.Index` is the array index** - Use it directly for GPU array access
✅ **Matches pool internal storage** - The index corresponds to the underlying pool array
✅ **No additional mapping needed** - For sparse allocation (with gaps)
✅ **Use dense packing** - If memory efficiency is critical and you don't mind rebuilding
✅ **Handle validity** - Check `handle.Valid` before using
✅ **Thread-safe** - GeometryPool operations are thread-safe

## Performance Tips

1. **Pre-allocate** - Size your GPU buffer based on maximum expected geometries
2. **Batch updates** - Update multiple bounds before uploading
3. **Use staging buffers** - For frequent updates
4. **Sparse is fine** - Modern GPUs handle sparse arrays well
5. **Update on change** - Only upload bounds when geometry actually changes

## Example: Complete Integration

```csharp
public class Scene
{
    private readonly GeometryPool _geometryPool = new();
    private readonly FrustumCullingManager _cullingManager;
    private readonly List<GeometryHandle> _geometryHandles = new();
    
    public void AddMesh(Mesh mesh)
    {
        // Create geometry
        var geometry = mesh.ToGeometry();
        
        // Add to pool - get handle with index
        var handle = _geometryPool.Create(geometry);
        
        // Store handle
        _geometryHandles.Add(handle);
        
        // The handle.Index is automatically the GPU array index!
        Console.WriteLine($"Geometry added at GPU index: {handle.Index}");
        
        // Upload updated bounds
        _cullingManager.UploadBoundsToGPU();
    }
    
    public void RemoveMesh(GeometryHandle handle)
    {
        _geometryPool.Destroy(ref handle);
        _geometryHandles.Remove(handle);
        
        // Note: This creates a gap at handle.Index in the array
        // Either:
        // 1. Leave the gap (GPU will skip it via instanceCount = 0)
        // 2. Rebuild with dense packing
    }
}
```

## Summary

**Simple answer**: Use `handle.Index` directly as your GPU array index!

```csharp
// When adding geometry
var handle = pool.Create(geometry);
boundsArray[handle.Index] = geometry.BoundingBoxLocal;

// In frustum culling shader
BoundingBox bbox = bounds[geometryHandle.Index];
```

No complex mapping required - the handle already contains the index you need!
