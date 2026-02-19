# Material Pool Integration with MaterialTypeRegistry

## Summary

The `MaterialPool` has been updated to integrate with the `MaterialTypeRegistry` system. Material IDs are now obtained directly from the `Material` objects themselves (which get their IDs from the `MaterialTypeRegistry`), rather than being managed separately by the pool.

## Changes Made

### 1. `MaterialHandle` Structure (IMaterialPool.cs)

**Before:**
```csharp
public readonly record struct MaterialHandle
{
    private readonly Handle<MaterialResource> _handle;

    public MaterialHandle(Handle<MaterialResource> handle, int id)
    {
        _handle = handle;
        MaterialId = id;
    }

    public int MaterialId { get; }
    // ...
}
```

**After:**
```csharp
public readonly record struct MaterialHandle
{
    private readonly Handle<MaterialResource> _handle;

    public MaterialHandle(Handle<MaterialResource> handle)
    {
        _handle = handle;
    }
    
    // MaterialId removed - retrieved from Material object instead
    // ...
}
```

**Rationale:**
- The `MaterialId` is already stored in the `Material` class (obtained from `MaterialTypeRegistry`)
- No need to duplicate this information in the handle
- Cleaner separation of concerns

### 2. New Methods in `IMaterialPool` Interface

Added methods to retrieve material IDs from handles:

```csharp
/// <summary>
/// Gets the material ID associated with the given handle.
/// </summary>
/// <param name="handle">The handle to the material.</param>
/// <returns>The material ID, or null if not found.</returns>
uint? GetMaterialId(in MaterialHandle handle);

/// <summary>
/// Tries to get the material ID associated with the given handle.
/// </summary>
/// <param name="handle">The handle to the material.</param>
/// <param name="materialId">The material ID if found; otherwise 0.</param>
/// <returns>True if the material was found; otherwise false.</returns>
bool TryGetMaterialId(in MaterialHandle handle, out uint materialId);
```

### 3. Implementation in `MaterialPool`

```csharp
public uint? GetMaterialId(in MaterialHandle handle)
{
    if (_disposed || handle.Empty)
    {
        return null;
    }

    lock (_lock)
    {
        var material = _pool.Get((Handle<MaterialResource>)handle);
        return material?.MaterialId;
    }
}

public bool TryGetMaterialId(in MaterialHandle handle, out uint materialId)
{
    var id = GetMaterialId(handle);
    if (id.HasValue)
    {
        materialId = id.Value;
        return true;
    }
    materialId = 0;
    return false;
}
```

### 4. Updated Documentation

Updated XML documentation comments to reflect the new system:
- Removed references to "automatic ID assignment"
- Added references to "MaterialTypeRegistry"
- Clarified that material IDs come from the registry

## Benefits

1. **Single Source of Truth**: Material IDs are managed exclusively by `MaterialTypeRegistry`
2. **Type Safety**: Material types must be registered before materials can be created
3. **Consistency**: All materials of the same type will have the same ID across the application
4. **Flexibility**: Easy to query material IDs without needing to cache them separately
5. **Cleaner API**: Simpler `MaterialHandle` structure without redundant data

## Usage Example

```csharp
// Register material types (typically at startup)
MaterialTypeRegistry.Register(new MaterialTypeRegistration
{
    TypeId = 1,
    Name = "PBR",
    OutputColorImplementation = "..."
});

// Create a material with the registered type
var material = new Material("PBR");  // Gets MaterialId = 1 from registry

// Add to pool
var pool = new MaterialPool();
var handle = pool.Create(material);

// Later, retrieve the material ID
if (pool.TryGetMaterialId(handle, out uint materialId))
{
    Console.WriteLine($"Material Type ID: {materialId}"); // Output: 1
}

// Or get the full material
var retrievedMaterial = pool.Get(handle);
Console.WriteLine($"Material Type ID: {retrievedMaterial.MaterialId}"); // Output: 1
```

## Migration Notes

If you have existing code that used `MaterialHandle.MaterialId`:

**Before:**
```csharp
var handle = pool.Create(material);
int id = handle.MaterialId;  // No longer available
```

**After:**
```csharp
var handle = pool.Create(material);
uint id = pool.GetMaterialId(handle) ?? 0;  // Option 1: Query from pool

// Or, if you already have the material reference:
uint id = material.MaterialId;  // Option 2: Get from material directly
```

## Compatibility

These changes are **breaking** for code that relied on `MaterialHandle.MaterialId`. However, the migration path is straightforward as shown above.

## Related Systems

- **MaterialTypeRegistry**: Central registry for material types and their IDs
- **Material**: Base class that stores the MaterialId obtained from the registry
- **MaterialPool**: Manages material instances and provides lifecycle management
- **MaterialHandle**: Lightweight handle for accessing materials in the pool
