using static HelixToolkit.Nex.Pool<
    HelixToolkit.Nex.Material.MaterialPropertyResource,
    HelixToolkit.Nex.Shaders.PBRProperties
>;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Manages a pool of material resources with automatic ID assignment and lifecycle management.
/// </summary>
/// <remarks>
/// The material pool provides:
/// <list type="bullet">
/// <item>Automatic material ID retrieval from MaterialTypeRegistry</item>
/// <item>Generation numbers to prevent the ABA problem</item>
/// <item>Automatic disposal of material pipelines when destroyed</item>
/// <item>Thread-safe operations</item>
/// </list>
/// </remarks>
public sealed class MaterialPropertyManager : IMaterialPropertyManager
{
    private static PBRProperties _defaultProperties = MaterialProperties.DefaultProperties;
    private readonly Pool<MaterialPropertyResource, PBRProperties> _pool = new();
    private readonly object _lock = new();

    public int Count => _pool.Count;

    public MaterialProperties Create(string materialName)
    {
        return Create(materialName, ref _defaultProperties);
    }

    public MaterialProperties Create(string materialName, ref PBRProperties properties)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNullOrEmpty(materialName);
        if (!MaterialTypeRegistry.TryGetByName(materialName, out var registration))
        {
            throw new ArgumentException(
                $"Material type '{materialName}' is not registered.",
                nameof(materialName)
            );
        }
        lock (_lock)
        {
            return new MaterialProperties(registration!.TypeId, ref properties, _pool);
        }
    }

    public MaterialProperties Create(MaterialTypeId materialTypeId)
    {
        return Create(materialTypeId, ref _defaultProperties);
    }

    public MaterialProperties Create(MaterialTypeId materialTypeId, ref PBRProperties properties)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!MaterialTypeRegistry.HasTypeId(materialTypeId))
        {
            throw new ArgumentException(
                $"Material type ID '{materialTypeId.Id}' is not registered.",
                nameof(materialTypeId)
            );
        }
        lock (_lock)
        {
            return new MaterialProperties(materialTypeId, ref properties, _pool);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pool.Clear();
        }
    }

    public IReadOnlyList<PoolEntry> Objects => _pool.Objects;

    public ref PBRProperties At(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pool.Objects.Count <= index || index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Index {index} is out of range. Valid range is 0 to {_pool.Objects.Count - 1}."
            );
        }
        return ref _pool.GetRef(index);
    }

    #region IDisposable Support

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Clear();
        _disposed = true;
    }
    #endregion
}
