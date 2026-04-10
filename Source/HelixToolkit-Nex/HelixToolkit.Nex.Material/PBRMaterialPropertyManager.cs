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
public sealed class PBRMaterialPropertyManager : IPBRMaterialPropertyManager
{
    private static PBRProperties _defaultProperties = PBRMaterialProperties.DefaultProperties;
    private readonly Pool<MaterialPropertyResource, PBRProperties> _pool = new();
    private readonly object _lock = new();

    public int Count => _pool.Count;

    public PBRMaterialProperties Create(string materialName)
    {
        return Create(materialName, ref _defaultProperties);
    }

    public PBRMaterialProperties Create(string materialName, ref PBRProperties properties)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNullOrEmpty(materialName);
        if (!PBRMaterialTypeRegistry.TryGetByName(materialName, out var registration))
        {
            throw new ArgumentException(
                $"Material type '{materialName}' is not registered.",
                nameof(materialName)
            );
        }
        lock (_lock)
        {
            return new PBRMaterialProperties(registration!.TypeId, ref properties, _pool);
        }
    }

    public PBRMaterialProperties Create(MaterialTypeId materialTypeId)
    {
        return Create(materialTypeId, ref _defaultProperties);
    }

    public PBRMaterialProperties Create(MaterialTypeId materialTypeId, ref PBRProperties properties)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PBRMaterialTypeRegistry.HasTypeId(materialTypeId))
        {
            throw new ArgumentException(
                $"Material type ID '{materialTypeId.Id}' is not registered.",
                nameof(materialTypeId)
            );
        }
        lock (_lock)
        {
            return new PBRMaterialProperties(materialTypeId, ref properties, _pool);
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
