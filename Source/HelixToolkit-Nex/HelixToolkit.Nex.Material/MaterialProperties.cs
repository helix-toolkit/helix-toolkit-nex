namespace HelixToolkit.Nex.Material;

public struct MaterialPropertyResource { }

public class MaterialProperties : IDisposable
{
    private readonly Pool<MaterialPropertyResource, PBRProperties>? _pool;
    private readonly Handle<MaterialPropertyResource> _handle =
        Handle<MaterialPropertyResource>.Null;

    public readonly uint MaterialTypeId = 0;

    public ref PBRProperties Properties => ref _pool!.GetRef(_handle)!;

    public bool Valid => _pool is not null && _handle.Valid;

    public uint Index => _handle.Index;

    public MaterialProperties(
        uint materialTypeId,
        Pool<MaterialPropertyResource, PBRProperties> pool
    )
    {
        MaterialTypeId = materialTypeId;
        _pool = pool;
        _handle = _pool.Create(new PBRProperties());
    }

    private MaterialProperties() { }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _pool?.Destroy(_handle);
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MaterialProperties()
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

    public static readonly MaterialProperties Null = new();
}
