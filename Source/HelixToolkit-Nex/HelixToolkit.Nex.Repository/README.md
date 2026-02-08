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
