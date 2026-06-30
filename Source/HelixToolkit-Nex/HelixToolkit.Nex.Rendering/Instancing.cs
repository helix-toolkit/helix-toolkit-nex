using System.ComponentModel;

namespace HelixToolkit.Nex.Geometries;

public static class InstanceTransformExts
{
    public static readonly InstanceTransform Identity = new()
    {
        Quaternion = Quaternion.Identity.ToVector4(),
        Scale = 1,
        Translation = Vector3.Zero,
    };

    public static InstanceTransform SetRotation(
        this InstanceTransform transform,
        Quaternion rotation
    )
    {
        transform.Quaternion = rotation.ToVector4();
        return transform;
    }

    public static InstanceTransform SetScale(this InstanceTransform transform, float scale)
    {
        transform.Scale = scale;
        return transform;
    }

    public static InstanceTransform SetTranslation(
        this InstanceTransform transform,
        Vector3 translation
    )
    {
        transform.Translation = translation;
        return transform;
    }

    public static Matrix4x4 ToMatrix(this InstanceTransform transform)
    {
        var m = transform.Quaternion.ToQuaternion().ToMatrix();
        m.Translation = transform.Translation;
        MatrixHelper.SetScaleVector(ref m, new Vector3(transform.Scale));
        return m;
    }
}

public partial class Instancing : HxObservableObject, IDisposable
{
    public static readonly InstanceTransform Identity = new()
    {
        Quaternion = Quaternion.Identity.ToVector4(),
        Scale = 1,
        Translation = Vector3.Zero,
    };

    [Observable]
    private FastList<InstanceTransform> _transforms = [];

    private bool _dirty = true;
    public ElementBuffer<InstanceTransform>? Buffer { private set; get; }
    public ElementBuffer<uint>? CulledIndicesBuffer { private set; get; }

    /// <summary>
    /// Back-reference to the <see cref="InstancingManager"/> that currently owns this instancing,
    /// or <c>null</c> when the instancing is not managed by any manager.
    /// </summary>
    internal InstancingManager? Manager { get; set; }

    /// <summary>
    /// Gets a value indicating whether this instancing is currently managed by an
    /// <see cref="InstancingManager"/>.
    /// </summary>
    public bool IsManaged => Manager is not null;

    /// <summary>
    /// Gets a value indicating whether this instancing's GPU buffers need to be re-uploaded.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Clears the dirty state after a successful GPU buffer upload.
    /// </summary>
    internal void ClearDirty() => _dirty = false;

    public bool IsDynamic { get; }

    public string Name { get; }

    public Instancing(bool isDynamic, string? name = null)
    {
        IsDynamic = isDynamic;
        Name = name ?? $"Instancing_{GetHashCode():X}";
        PropertyChanged += Instancing_PropertyChanged;
    }

    private void Instancing_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _dirty = true;
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public ResultCode UpdateBuffer(IContext context)
    {
        if (!_dirty)
            return ResultCode.Ok;
        if (_transforms.Count > Limits.MaxInstanceCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Transforms),
                $"Instance count {_transforms.Count} exceeds the maximum allowed {Limits.MaxInstanceCount}."
            );
        }
        Buffer ??= new ElementBuffer<InstanceTransform>(
            context,
            _transforms.Count,
            BufferUsageBits.Storage,
            IsDynamic,
            debugName: Name
        );
        Buffer.Upload(_transforms);
        CulledIndicesBuffer ??= new ElementBuffer<uint>(
            context,
            _transforms.Count,
            BufferUsageBits.Storage,
            IsDynamic,
            debugName: $"{Name}_CulledInstIndices"
        );
        CulledIndicesBuffer.EnsureCapacity(_transforms.Count);
        _dirty = false;
        return ResultCode.Ok;
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Disposal coordination (Requirements 13.3, 5.1): when a MANAGED instancing is disposed
                // directly by user code, route the disposal through the owning manager's deferred-removal
                // path so the pool bookkeeping and GPU-resource disposal happen at a controlled point
                // (InstancingManager.ProcessPendingRemovals -> Remove). Return WITHOUT freeing the GPU
                // buffers and WITHOUT setting _disposedValue, so the deferred Remove can later actually
                // dispose the buffers. InstancingManager.Remove clears Manager (sets it to null) BEFORE
                // calling instancing.Dispose(), so when Remove disposes this instancing the Manager is
                // null and disposal proceeds to free the GPU buffers below without re-entering
                // RemoveDeferred.
                var manager = Manager;
                if (manager is not null)
                {
                    manager.RemoveDeferred(this);
                    return;
                }

                Buffer?.Dispose();
                Buffer = null;
                CulledIndicesBuffer?.Dispose();
                CulledIndicesBuffer = null;
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Instancing()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
