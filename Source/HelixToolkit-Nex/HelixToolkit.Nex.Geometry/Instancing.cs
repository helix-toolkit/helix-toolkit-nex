namespace HelixToolkit.Nex.Geometries;

public partial class Instancing : ObservableObject, IDisposable
{
    [Observable]
    private FastList<Matrix4x4> _transforms = [];

    private bool _dirty = true;
    public ElementBuffer<Matrix4x4>? Buffer { private set; get; }
    public ElementBuffer<uint>? CulledIndicesBuffer { private set; get; }

    public bool IsDynamic { get; }

    public Instancing(bool isDynamic)
    {
        IsDynamic = isDynamic;
        PropertyChanged += Instancing_PropertyChanged;
    }

    private void Instancing_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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
        Buffer ??= new ElementBuffer<Matrix4x4>(
            context,
            _transforms.Count,
            BufferUsageBits.Storage,
            IsDynamic
        );
        Buffer.Upload(_transforms);
        CulledIndicesBuffer ??= new ElementBuffer<uint>(
            context,
            _transforms.Count,
            BufferUsageBits.Storage,
            IsDynamic
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
