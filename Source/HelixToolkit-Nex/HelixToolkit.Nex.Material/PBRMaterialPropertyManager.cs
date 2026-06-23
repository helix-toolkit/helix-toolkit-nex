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
        ArgumentException.ThrowIfNullOrEmpty(materialName);
        lock (_lock)
        {
            return new PBRMaterialProperties(materialName, ref properties, _pool);
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
        if (index > _pool.LastObjectIndex || index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Index {index} is out of range. Valid range is 0 to {_pool.LastObjectIndex}."
            );
        }
        return ref _pool.GetRef(index);
    }

    public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!buffer.HostVisible)
        {
            throw new ArgumentException(
                "Buffer must be host visible for dynamic uploads.",
                nameof(buffer)
            );
        }
        lock (_lock)
        {
            return buffer.WriteDynamic(
                _pool.LastObjectIndex + 1,
                _pool,
                static (ctx, pool) =>
                {
                    for (var i = 0; i <= pool.LastObjectIndex; ++i)
                    {
                        ctx.Write(ref pool.GetRef(i));
                    }
                }
            );
        }
    }

    public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer, IEnumerable<uint> indices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!buffer.HostVisible)
        {
            throw new ArgumentException(
                "Buffer must be host visible for dynamic uploads.",
                nameof(buffer)
            );
        }
        lock (_lock)
        {
            return buffer.WriteDynamic(
                _pool.LastObjectIndex + 1,
                (_pool, indices),
                static (ctx, values) =>
                {
                    var (pool, indices) = values;
                    foreach (var index in indices)
                    {
                        if (index > pool.LastObjectIndex)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(indices),
                                $"Index {index} is out of range. Valid range is 0 to {pool.LastObjectIndex}."
                            );
                        }
                        ctx.WriteElement(ref pool.GetRef((int)index), (int)index);
                    }
                }
            );
        }
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
