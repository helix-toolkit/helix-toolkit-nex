```markdown
# HelixToolkit.Nex.Repository

The `HelixToolkit.Nex.Repository` package provides a robust, thread-safe caching mechanism for various GPU resources such as textures, shaders, and samplers. It is designed to optimize resource management in the HelixToolkit.Nex engine by implementing an LRU (Least Recently Used) eviction policy, ensuring efficient memory usage and performance.

## Overview

The repository package is a critical component of the HelixToolkit.Nex engine, responsible for managing the lifecycle of GPU resources. It provides:
- Automatic deduplication of resources to prevent redundant GPU resource creation.
- Thread-safe access to cached resources, ensuring consistency and performance in multi-threaded environments.
- LRU eviction policy to manage memory usage effectively.
- Optional time-based expiration for cached entries.
- Comprehensive cache statistics for monitoring and debugging.

## Key Types

| Type | Description |
|------|-------------|
| `IRepository<TKey, TEntry, TResource>` | Interface for a generic repository that caches resources with LRU eviction. |
| `ISamplerRepository` | Interface for caching sampler state resources. |
| `IShaderRepository` | Interface for caching compiled SPIR-V shader modules. |
| `ITextureRepository` | Interface for caching GPU texture resources. |
| `Repository<TKey, TEntry, TResource>` | Abstract class implementing the generic repository pattern. |
| `SamplerRepository` | Concrete implementation for caching sampler resources. |
| `ShaderRepository` | Concrete implementation for caching shader modules. |
| `TextureRepository` | Concrete implementation for caching texture resources. |
| `SamplerRef` | Wrapper for a live sampler resource with disposal notification. |
| `TextureRef` | Wrapper for a live texture resource with disposal notification. |
| `CacheEntry<TResource>` | Represents a cache entry for a resource. |
| `RepositoryStatistics` | Provides statistics about the repository cache. |

## Usage Examples

### Caching a Texture from a Stream

```csharp
using HelixToolkit.Nex.Repository;
using System.IO;

var context = /* Obtain IContext instance */;
var textureRepo = new TextureRepository(context);

using var stream = File.OpenRead("path/to/texture.png");
var textureRef = textureRepo.GetOrCreateFromStream("uniqueTextureName", stream);
```

### Caching a Shader Module from GLSL Source

```csharp
using HelixToolkit.Nex.Repository;

var context = /* Obtain IContext instance */;
var shaderRepo = new ShaderRepository(context);

string glslSource = "/* GLSL shader code */";
var shaderModule = shaderRepo.GetOrCreateFromGlsl(ShaderStage.Vertex, glslSource);
```

### Caching a Sampler Resource

```csharp
using HelixToolkit.Nex.Repository;

var context = /* Obtain IContext instance */;
var samplerRepo = new SamplerRepository(context);

var samplerDesc = new SamplerStateDesc
{
    MinFilter = Filter.Linear,
    MagFilter = Filter.Linear,
    WrapU = WrapMode.Repeat,
    WrapV = WrapMode.Repeat
};

var samplerRef = samplerRepo.GetOrCreate(samplerDesc);
```

### Asynchronously Caching a Texture from a File

```csharp
using HelixToolkit.Nex.Repository;
using System.Threading.Tasks;

var context = /* Obtain IContext instance */;
var textureRepo = new TextureRepository(context);

var textureRef = await textureRepo.GetOrCreateFromFileAsync("path/to/texture.png");
```

## Architecture Notes

- **Design Patterns**: The repository pattern is used extensively to manage resource caching. Each repository type (e.g., `SamplerRepository`, `ShaderRepository`, `TextureRepository`) implements a specific interface, providing a consistent API for resource management.
- **Dependencies**: The repositories depend on the `HelixToolkit.Nex.Graphics` package for context and resource creation. They also utilize `Microsoft.Extensions.Logging` for logging purposes.
- **LRU Eviction**: The repositories implement an LRU eviction policy to ensure that the most recently used resources are retained while older, less frequently accessed resources are evicted when the cache reaches its capacity.
- **Thread Safety**: All public methods are thread-safe, allowing concurrent access and modification of the cache without data races or inconsistencies.
- **Resource Wrappers**: `SamplerRef` and `TextureRef` provide a safe way to handle GPU resources, ensuring that resources are properly disposed and notifying consumers when resources are no longer valid.

## Configuration

The project now supports additional build configurations for Linux:
- `LinuxDebug`
- `LinuxRelease`

These configurations allow for building and testing the repository package on Linux platforms, expanding the versatility and deployment options for the HelixToolkit.Nex engine.
```
