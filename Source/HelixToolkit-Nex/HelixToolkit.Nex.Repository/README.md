# HelixToolkit.Nex.Repository - Thread-Safe Resource Caching

## Overview

The `HelixToolkit.Nex.Repository` library provides a generic, thread-safe caching layer for GPU resources in HelixToolkit.Nex. It includes:

- **`IRepository<TKey, TEntry, TResource>`**: Generic interface for resource caches
- **`Repository<TKey, TEntry, TResource>`**: Generic base class for implementing resource caches
- **`IShaderRepository`**: Interface for shader module caching
- **`ShaderRepository`**: Specialized implementation for caching compiled SPIR-V shader modules

This library sits on top of the graphics API to provide caching for final compiled GPU resources, with automatic deduplication, lifecycle management, and LRU eviction.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Application Code                                       │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  IShaderRepository (Interface)                          │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  ShaderRepository (SPIR-V Shader Module Cache)          │
│  - Inherits from Repository<string, ...>                │
│  - Implements IShaderRepository                         │
│  - Shader-specific functionality                        │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  IRepository<TKey, TEntry, TResource> (Interface)       │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  Repository<TKey, TEntry, TResource> (Generic Base)     │
│  - Thread-safe ConcurrentDictionary                     │
│  - LRU eviction policy                                  │
│  - Cache statistics                                     │
│  - Expiration handling                                  │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  IContext.CreateShaderModule() / Other Create Methods   │
│  - Creates GPU resources                                │
│  - Compiles GLSL → SPIR-V (if needed)                  │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│  ShaderCache (GLSL Source Code Cache)                   │
│  - Caches preprocessed GLSL source                      │
└─────────────────────────────────────────────────────────┘
```

## Interfaces

### IRepository<TKey, TEntry, TResource>

Generic interface providing the core contract for resource repositories:

```csharp
public interface IRepository<TKey, TEntry, TResource> : IDisposable
    where TKey : notnull
    where TEntry : CacheEntry<TResource>
    where TResource : class, IDisposable
{
    int Count { get; }
    bool TryGet(TKey cacheKey, out TEntry? entry);
    void Clear();
    int CleanupExpired();
    RepositoryStatistics GetStatistics();
}
```

**Benefits:**
- Enables dependency injection and testing
- Provides abstraction for different caching implementations
- Supports mocking in unit tests
- Allows for custom implementations

### IShaderRepository

Specialized interface for shader module caching:

```csharp
public interface IShaderRepository : IDisposable
{
    int Count { get; }
    ShaderModuleResource GetOrCreateFromGlsl(ShaderStage stage, string glslSource, ShaderDefine[]? defines = null, string? debugName = null);
    ShaderModuleResource GetOrCreateFromSpirv(ShaderStage stage, nint spirvData, uint spirvSize, string? debugName = null);
    bool TryGet(string cacheKey, out ShaderModuleCacheEntry? entry);
    void Clear();
    int CleanupExpired();
    RepositoryStatistics GetStatistics();
}
```

**Benefits:**
- Clean API for shader-specific operations
- Enables dependency injection in rendering systems
- Simplifies testing and mocking
- Future-proof for alternative implementations

## Usage with Dependency Injection

### Registering the Repository

```csharp
using Microsoft.Extensions.DependencyInjection;
using HelixToolkit.Nex.Repository;

var services = new ServiceCollection();

// Register as singleton (recommended)
services.AddSingleton<IShaderRepository>(provider => 
    new ShaderRepository(provider, maxEntries: 500, expirationTime: TimeSpan.FromMinutes(30))
);

var serviceProvider = services.BuildServiceProvider();
```

### Consuming via Interface

```csharp
public class MaterialSystem
{
    private readonly IShaderRepository _shaderRepo;
    private readonly IContext _context;

    // Constructor injection
    public MaterialSystem(IShaderRepository shaderRepo, IContext context)
    {
        _shaderRepo = shaderRepo;
        _context = context;
    }

    public RenderPipelineResource CreatePipeline(string vertexShader, string fragmentShader)
    {
        // Use interface methods
        var vs = _shaderRepo.GetOrCreateFromGlsl(ShaderStage.Vertex, vertexShader);
        var fs = _shaderRepo.GetOrCreateFromGlsl(ShaderStage.Fragment, fragmentShader);

        return _context.CreateRenderPipeline(new RenderPipelineDesc
        {
            VertexShader = vs,
            FragementShader = fs,
        });
    }
}
```

## Testing with Interfaces

### Creating Mock Implementations

```csharp
using Moq;

// Mock the shader repository
var mockRepo = new Mock<IShaderRepository>();
mockRepo.Setup(r => r.GetOrCreateFromGlsl(
    It.IsAny<ShaderStage>(),
    It.IsAny<string>(),
    It.IsAny<ShaderDefine[]>(),
    It.IsAny<string>()
)).Returns(someShaderModule);

// Use in tests
var materialSystem = new MaterialSystem(mockRepo.Object, context);
```

### Integration Testing

```csharp
[TestClass]
public class ShaderRepositoryTests
{
    private IShaderRepository _repository;
    private IContext _context;

    [TestInitialize]
    public void Setup()
    {
        _context = CreateTestContext();
        _repository = new ShaderRepository(_context, maxEntries: 10);
    }

    [TestMethod]
    public void TestCaching()
    {
        // First call - cache miss
        var shader1 = _repository.GetOrCreateFromGlsl(ShaderStage.Fragment, testShaderCode);
        
        // Second call - cache hit
        var shader2 = _repository.GetOrCreateFromGlsl(ShaderStage.Fragment, testShaderCode);
        
        Assert.AreSame(shader1, shader2);
        
        var stats = _repository.GetStatistics();
        Assert.AreEqual(1, stats.TotalMisses);
        Assert.AreEqual(1, stats.TotalHits);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _repository.Dispose();
        _context.Dispose();
    }
}
```

## Generic Repository

### Overview

The `Repository<TKey, TEntry, TResource>` base class provides common functionality for caching any GPU resource type:

```csharp
public abstract class Repository<TKey, TEntry, TResource> : IDisposable
    where TKey : notnull
    where TEntry : CacheEntry<TResource>
    where TResource : class, IDisposable
```

### Key Features

#### 1. **Thread Safety**
- Uses `ConcurrentDictionary` for lock-free read operations
- `ReaderWriterLockSlim` for eviction operations
- Atomic operations for cache statistics (`Interlocked` operations)
- Safe for concurrent access from multiple threads

#### 2. **LRU Eviction**
- When cache is full, evicts least recently used entry
- Considers both `LastAccessedAt` and `AccessCount`
- Automatically disposes of evicted resources

#### 3. **Expiration Support**
- Supports time-based expiration (default: no expiration)
- Expired entries are automatically cleaned up on access

#### 4. **Statistics**
- Track cache hits and misses
- Monitor access patterns
- Get oldest/newest entries
- Calculate hit rates

### Creating Custom Repositories

To create a custom repository for other resource types:

```csharp
// 1. Define your cache entry type
public sealed class TextureCacheEntry : CacheEntry<TextureResource>
{
    public required Format Format { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
}

// 2. Implement your repository
public sealed class TextureRepository : Repository<string, TextureCacheEntry, TextureResource>
{
    private readonly IContext _context;

    public TextureRepository(IContext context, int maxEntries = 500, TimeSpan? expirationTime = null)
        : base(maxEntries, expirationTime)
    {
        _context = context;
    }

    protected override void DisposeEntry(TextureCacheEntry entry)
    {
        entry.Resource.Dispose();
    }

    protected override void AddResourceReference(TextureResource resource)
    {
        resource.AddReference();
    }

    public TextureResource GetOrCreate(string key, TextureDesc desc, string? debugName = null)
    {
        if (TryGet(key, out var cached))
        {
            AddResourceReference(cached!.Resource);
            return cached!.Resource;
        }

        // Create new texture
        var result = _context.CreateTexture(desc, out var texture, debugName);
        if (result != ResultCode.Ok)
        {
            throw new InvalidOperationException($"Failed to create texture: {result}");
        }

        var entry = new TextureCacheEntry
        {
            Resource = texture,
            Format = desc.Format,
            Width = desc.Width,
            Height = desc.Height,
            SourceHash = ComputeHash($"{desc.Width}x{desc.Height}x{desc.Format}"),
            DebugName = debugName,
            AccessCount = 1,
        };

        Set(key, entry);
        AddResourceReference(texture);
        return texture;
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
```

## ShaderRepository

The `ShaderRepository` is a specialized implementation for caching compiled SPIR-V shader modules.

`ShaderRepository` is a thread-safe caching layer for compiled SPIR-V shader modules in HelixToolkit.Nex. It sits on top of the existing `ShaderCache` (which caches preprocessed GLSL source code) to provide caching for the final compiled GPU shader resources.

### Key Features

The `ShaderRepository` builds upon the generic repository with shader-specific functionality:
- **Shader-specific cache key generation**: Computes keys from shader stage, source, and defines
- **Automatic GPU resource creation**: Integrates with `IContext.CreateShaderModule()`
- **Debug name support**: Optional debug naming for shaders

### Example

```csharp
public class MyShaderRepository : ShaderRepository
{
    public MyShaderRepository(IContext context, int maxEntries)
        : base(maxEntries)
    {
    }

    protected override void DisposeEntry(ShaderModuleCacheEntry entry)
    {
        // Custom disposal logic (if needed)
        entry.ShaderModule.Dispose();
    }

    protected override void AddResourceReference(ShaderModuleResource resource)
    {
        // Custom reference counting (if needed)
        resource.AddReference();
    }
}
```

# Resource Management System

## Overview

The Resource Management System provides an efficient way to store and manage **geometries** and **materials** in game engines using an ID-based approach with automatic lifecycle management.

## Architecture

### Key Concepts

**Scene nodes DO NOT store mesh/material data directly.** Instead, they store lightweight **handles** (IDs) that reference resources in centralized pools.


Scene Graph (Nodes)              Resource Pools
─────────────────────           ────────────────────
Node A                          Geometry Pool
 └─ MeshRender                  ┌─────────────────┐
    ├─ GeometryHandle ────────► │ [0] Cube        │
    │    Index: 0                │ [1] Sphere      │
    │    Gen: 1                  │ [2] Dragon      │
    └─ MaterialHandle ─────┐     └─────────────────┘
         Index: 5          │     
         Gen: 1            │     Material Pool
                           │     ┌─────────────────┐
Node B                     └────►│ [5] MetalPBR    │
 └─ MeshRender                   │ [6] GlassPBR    │
    ├─ GeometryHandle ────────► │ [7] WoodPBR     │
    │    Index: 0 (shared!)      └─────────────────┘
    └─ MaterialHandle ─────┐
         Index: 5 (shared!)
```

### Why Use Handles Instead of Direct References?

#### ✅ Benefits

1. **Memory Efficiency**: Multiple nodes can reference the same resource
2. **Cache Performance**: Better data locality for rendering systems
3. **Easy Serialization**: Just save/load IDs instead of full data
4. **Hot-Reloading**: Swap resources without modifying scene graph
5. **Resource Sharing**: 100 trees can share 1 mesh + 1 material
6. **GPU Optimization**: Batch rendering by sorting by resource IDs

#### ⚠️ Trade-offs

- Indirect access (one extra lookup)
- Need to validate handles before use
- Must manage resource lifetimes carefully

## Core Components

### 1. GeometryHandle

A lightweight handle to a geometry resource:

```csharp
public readonly record struct GeometryHandle
{
    public uint Index { get; }  // Index in the geometry pool
    public uint Gen { get; }    // Generation number (prevents ABA problem)
    public bool Valid { get; }  // True if handle is valid
}
```

**Generation Numbers Prevent the ABA Problem:**

```csharp
// Create geometry
var handle1 = manager.CreateGeometry(cubeGeometry);
// Index: 0, Gen: 1

// Destroy it (ID 0 goes back to free list)
manager.DestroyGeometry(ref handle1);

// Create new geometry (reuses ID 0)
var handle2 = manager.CreateGeometry(sphereGeometry);
// Index: 0, Gen: 2  ← Different generation!

// Old handle is now invalid
var geo = manager.Geometries.Get(handle1);
// Returns null because Gen doesn't match
```

### 2. MaterialHandle

Similar to GeometryHandle but for materials:

```csharp
public readonly record struct MaterialHandle
{
    public uint Index { get; }
    public uint Gen { get; }
    public bool Valid { get; }
}
```

### 3. GeometryPool

Manages geometry resources with automatic ID allocation:

```csharp
var pool = new GeometryPool();

// Create geometry
var geometry = new Geometry { Vertices = [...], Indices = [...] };
var handle = pool.Create(geometry);

// Retrieve geometry
var retrieved = pool.Get(handle);

// Destroy geometry (ID returns to free list)
pool.Destroy(ref handle);
```

**Free-List Algorithm:**
- Destroyed IDs go into a free list
- New allocations first try to reuse freed IDs
- Only allocates new IDs when free list is empty
- O(1) allocation and deallocation

### 4. MaterialPool

Same as GeometryPool but for materials:

```csharp
var pool = new MaterialPool();

var material = new PBRMaterial();
var handle = pool.Create(material);
var retrieved = pool.Get(handle);
pool.Destroy(ref handle);
```

### 5. ResourceManager

High-level API that combines both pools and handles GPU resource creation:

```csharp
var manager = new ResourceManager(context);

// Create geometry and automatically upload to GPU
var geoHandle = manager.CreateGeometry(geometry, uploadToGpu: true);

// Create material with pipeline
var matHandle = manager.CreateMaterial(material, pipelineDesc);

// Update dirty geometry buffers
manager.UpdateGeometryBuffers(geoHandle);

// Get statistics
var stats = manager.GetStatistics();
Console.WriteLine($"Geometries: {stats.GeometryCount}");
Console.WriteLine($"Materials: {stats.MaterialCount}");

// Clean up
manager.DestroyGeometry(ref geoHandle);
manager.DestroyMaterial(ref matHandle);
```

## Usage Examples

### Example 1: Basic Geometry Creation

```csharp
var manager = new ResourceManager(context);

// Create a cube geometry
var cubeGeometry = new Geometry
{
    Vertices = [
        new Vector4(-1, -1, -1, 1), new Vector4(1, -1, -1, 1),
        new Vector4(1, 1, -1, 1), new Vector4(-1, 1, -1, 1),
        // ... more vertices
    ],
    Indices = [0u, 1u, 2u, 2u, 3u, 0u, /* ... */]
};

// Create and upload to GPU
var cubeHandle = manager.CreateGeometry(cubeGeometry, uploadToGpu: true);

// Check GPU buffers were created
var cube = manager.Geometries.Get(cubeHandle);
Assert.IsTrue(cube.VertexBuffer.Valid);
Assert.IsTrue(cube.IndexBuffer.Valid);
```

### Example 2: Resource Sharing

```csharp
// Create one tree geometry
var treeGeometry = new Geometry { /* tree mesh data */ };
var treeHandle = manager.CreateGeometry(treeGeometry, uploadToGpu: true);

// Create different materials
var summerMaterial = new PBRMaterial { /* summer colors */ };
var autumnMaterial = new PBRMaterial { /* autumn colors */ };
var summerHandle = manager.CreateMaterial(summerMaterial);
var autumnHandle = manager.CreateMaterial(autumnMaterial);

// Create 50 summer trees (all share same geometry)
for (int i = 0; i < 50; i++)
{
    var node = new Node(world);
    node.Entity.Add(new MeshRender(treeHandle, summerHandle));
    // Set position, rotation, etc.
}

// Create 50 autumn trees (share same geometry, different material)
for (int i = 0; i < 50; i++)
{
    var node = new Node(world);
    node.Entity.Add(new MeshRender(treeHandle, autumnHandle));
}

// Memory usage: 1 geometry + 2 materials instead of 100 geometries!
```

### Example 3: Hot-Swapping Resources

```csharp
// Create initial setup
var lowPolyHandle = manager.CreateGeometry(lowPolyMesh, uploadToGpu: true);
var highPolyHandle = manager.CreateGeometry(highPolyMesh, uploadToGpu: true);

// Scene has many nodes using low-poly mesh
var nodes = GetAllMeshNodes();

// Switch all nodes to high-poly (instant!)
foreach (var node in nodes)
{
    var meshRender = node.Entity.Get<MeshRender>();
    node.Entity.Set(new MeshRender(highPolyHandle, meshRender.MaterialHandle));
}

// No memory allocation, no GPU uploads - just handle swap!
```

### Example 4: Deferred GPU Upload

```csharp
// Create many geometries without uploading to GPU
var handles = new List<GeometryHandle>();
for (int i = 0; i < 1000; i++)
{
    var geo = GenerateProcedural Geometry(i);
    var handle = manager.CreateGeometry(geo, uploadToGpu: false);
    handles.Add(handle);
}

// Later, upload all in batch
foreach (var handle in handles)
{
    manager.UpdateGeometryBuffers(handle);
}

// Or update all dirty geometries at once
int updated = manager.UpdateAllDirtyGeometries();
Console.WriteLine($"Updated {updated} geometries");
```

### Example 5: Resource Lifetime Management

```csharp
void LoadLevel(ResourceManager manager)
{
    var levelGeometries = new List<GeometryHandle>();
    var levelMaterials = new List<MaterialHandle>();
    
    // Load level resources
    foreach (var asset in levelAssets)
    {
        var geo = LoadGeometry(asset);
        var geoHandle = manager.CreateGeometry(geo, uploadToGpu: true);
        levelGeometries.Add(geoHandle);
        
        var mat = LoadMaterial(asset);
        var matHandle = manager.CreateMaterial(mat);
        levelMaterials.Add(matHandle);
    }
    
    // ... use resources ...
    
    // Unload level
    foreach (var handle in levelGeometries)
    {
        var h = handle;
        manager.DestroyGeometry(ref h);
    }
    foreach (var handle in levelMaterials)
    {
        var h = handle;
        manager.DestroyMaterial(ref h);
    }
}
```

## ID Management and Recycling

### How IDs Are Maintained

The system uses a **free-list** algorithm:

```
Initial state:
Pool: [empty]
Free list: HEAD → ∅

Create geometry A:
Pool: [A]
Free list: HEAD → ∅
Handle: (Index: 0, Gen: 1)

Create geometry B:
Pool: [A, B]
Free list: HEAD → ∅
Handle: (Index: 1, Gen: 1)

Destroy geometry A:
Pool: [freed, B]
Free list: HEAD → 0 → ∅
(A's slot is marked with next Gen: 2)

Create geometry C (reuses A's slot):
Pool: [C, B]
Free list: HEAD → ∅
Handle: (Index: 0, Gen: 2)  ← Same index, different generation!
```

### Generation Numbers Protect Against Use-After-Free

```csharp
// Create and destroy quickly
var handle1 = pool.Create(geometryA);  // Index: 0, Gen: 1
pool.Destroy(ref handle1);

var handle2 = pool.Create(geometryB);  // Index: 0, Gen: 2 (reused slot)

// Try to use old handle
var result = pool.Get(handle1);  // Returns null! Gen mismatch
```

## Performance Characteristics

### Time Complexity

| Operation | Complexity | Notes |
|-----------|-----------|-------|
| Create | O(1) | Uses free-list |
| Destroy | O(1) | Adds to free-list |
| Get | O(1) | Direct array access |
| Find | O(n) | Linear search |

### Memory Layout

```
Geometry Pool Entry:
┌──────────────┬──────────────┬──────────────────┐
│ Gen (uint)   │ NextFree     │ Geometry*        │
│ 4 bytes      │ 4 bytes      │ 8 bytes (64-bit) │
└──────────────┴──────────────┴──────────────────┘
Total: 16 bytes per entry

Handle:
┌──────────────┬──────────────┐
│ Index (uint) │ Gen (uint)   │
│ 4 bytes      │ 4 bytes      │
└──────────────┴──────────────┘
Total: 8 bytes
```

### Cache Performance

**Good:**
- Handles are small (8 bytes) - fit more in cache
- Resource data is contiguous in memory
- Sorting by handle ID improves GPU batching

**Could Be Better:**
- Indirect access requires pointer chase
- Generational index adds validation overhead

## Thread Safety

### Current Implementation

⚠️ **Thread-safe operations (protected by locks):**
- `Create()` - Can be called from any thread
- `Destroy()` - Can be called from any thread
- `Get()` - Can be called from any thread

⚠️ **Not thread-safe:**
- Modifying geometry/material data after creation
- Reading while another thread modifies

### Recommended Usage

```csharp
// ✅ Good: Create on main thread
var handle = manager.CreateGeometry(geometry);

// ✅ Good: Destroy on main thread
manager.DestroyGeometry(ref handle);

// ⚠️ Risky: Concurrent modification
// Thread 1:
var geo = manager.Geometries.Get(handle);
geo.Vertices.Add(newVertex);

// Thread 2:
var geo = manager.Geometries.Get(handle);
geo.Vertices.Clear();  // ❌ Race condition!

// ✅ Better: Synchronize modifications
lock (geometryLock)
{
    var geo = manager.Geometries.Get(handle);
    geo.Vertices.Add(newVertex);
}
```

## Integration with Rendering

### Mesh Rendering Pipeline

```csharp
// Setup phase (once)
var resourceManager = new ResourceManager(context);
var geometry = LoadCubeGeometry();
var material = CreatePBRMaterial();

var geoHandle = resourceManager.CreateGeometry(geometry, uploadToGpu: true);
var matHandle = resourceManager.CreateMaterial(material, pipelineDesc);

// Scene setup
var node = new Node(world);
node.Entity.Add(new MeshRender(geoHandle, matHandle));

// Render loop
foreach (var entity in query.GetEntities())
{
    var meshRender = entity.Get<MeshRender>();
    
    // Retrieve resources
    var geo = resourceManager.Geometries.Get(meshRender.GeometryHandle);
    var mat = resourceManager.Materials.Get(meshRender.MaterialHandle);
    
    // Bind and draw
    cmdBuffer.BindRenderPipeline(mat.Pipeline);
    cmdBuffer.BindVertexBuffer(0, geo.VertexBuffer);
    cmdBuffer.BindIndexBuffer(geo.IndexBuffer, IndexFormat.UInt32);
    cmdBuffer.DrawIndexed(geo.IndexCount);
}
```

## Best Practices

### ✅ DO

1. **Create resources at load time** - Avoid creating during rendering
2. **Share resources** - Use same geometry/material for multiple nodes
3. **Check handle validity** - Always check `handle.Valid` before use
4. **Destroy when done** - Free resources to allow ID recycling
5. **Use statistics** - Monitor resource usage with `GetStatistics()`
6. **Batch updates** - Use `UpdateAllDirtyGeometries()` for bulk updates

### ❌ DON'T

1. **Don't keep stale handles** - Check validity after destroy
2. **Don't compare handles directly** - Use `.Index` and `.Gen` explicitly
3. **Don't assume ID order** - IDs can be reused in any order
4. **Don't modify resources during rendering** - Can cause GPU sync issues
5. **Don't leak handles** - Always destroy when done

## Debugging Tips

### Enable Debug Output

```csharp
var stats = manager.GetStatistics();
Console.WriteLine($"Active Geometries: {stats.GeometryCount}");
Console.WriteLine($"Active Materials: {stats.MaterialCount}");
Console.WriteLine($"Dirty Geometries: {stats.DirtyGeometryCount}");
```

### Validate Handles

```csharp
void DrawMesh(GeometryHandle geoHandle, MaterialHandle matHandle)
{
    if (!geoHandle.Valid || !matHandle.Valid)
    {
        Console.WriteLine("Invalid handle!");
        return;
    }
    
    var geo = manager.Geometries.Get(geoHandle);
    if (geo == null)
    {
        Console.WriteLine($"Geometry not found: Index={geoHandle.Index}, Gen={geoHandle.Gen}");
        return;
    }
    
    // ... continue
}
```

### Track Resource Leaks

```csharp
class ResourceTracker
{
    private HashSet<GeometryHandle> _trackedGeometries = new();
    
    public GeometryHandle TrackGeometry(Geometry geo)
    {
        var handle = manager.CreateGeometry(geo);
        _trackedGeometries.Add(handle);
        return handle;
    }
    
    public void CheckLeaks()
    {
        var activeCount = manager.Geometries.GetAllHandles().Count();
        var trackedCount = _trackedGeometries.Count;
        
        if (activeCount != trackedCount)
        {
            Console.WriteLine($"Leak detected! Active: {activeCount}, Tracked: {trackedCount}");
        }
    }
}
```

## Future Enhancements

Potential improvements:

- [ ] **Reference counting** - Automatic cleanup when no nodes reference resource
- [ ] **Weak handles** - Handles that don't prevent resource cleanup
- [ ] **Resource streaming** - Load/unload based on distance/frustum
- [ ] **Resource aliasing** - Multiple handles to same resource with different configs
- [ ] **Memory pooling** - Pre-allocate geometry/material memory
- [ ] **GPU-driven culling** - Sort and batch by resource IDs on GPU

## See Also

- `GeometryPool.cs` - Geometry resource pool implementation
- `MaterialPool.cs` - Material resource pool implementation
- `ResourceManager.cs` - High-level resource management API
- `MeshRender.cs` - Mesh render component using handles
- `ResourceManagerTests.cs` - Comprehensive test suite
