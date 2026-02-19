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
    private readonly Pool<MaterialPropertyResource, PBRProperties> _pool = new();
    private readonly object _lock = new();

    public int Count => _pool.Count;

    public MaterialProperties Create(string materialName)
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
            return new MaterialProperties(registration!.TypeId, _pool);
        }
    }

    public MaterialProperties Create(MaterialTypeId materialTypeId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!MaterialTypeRegistry.HasTypeId(materialTypeId.Id))
        {
            throw new ArgumentException(
                $"Material type ID '{materialTypeId.Id}' is not registered.",
                nameof(materialTypeId)
            );
        }
        return new MaterialProperties(materialTypeId, _pool);
    }

    public void Clear()
    {
        _pool.Clear();
    }

    public IReadOnlyList<PoolEntry> Objects => _pool.Objects;

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
