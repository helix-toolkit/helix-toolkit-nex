namespace HelixToolkit.Nex.Material;

public struct MaterialPropertyResource { }

public enum MaterialPropertyOp
{
    Create,
    Update,
    Destroy,
}

public readonly struct MaterialPropsUpdatedEvent(
    MaterialTypeId materialTypeId,
    uint index,
    MaterialPropertyOp operation
) : IEvent
{
    public MaterialTypeId MaterialTypeId { get; } = materialTypeId;
    public uint Index { get; } = index;
    public MaterialPropertyOp Operation { get; } = operation;
}

public sealed class MaterialProperties : IDisposable
{
    private static readonly EventBus _eventBus = EventBus.Instance;
    private readonly Pool<MaterialPropertyResource, PBRProperties>? _pool;
    private readonly Handle<MaterialPropertyResource> _handle =
        Handle<MaterialPropertyResource>.Null;
    private static readonly PBRProperties _defaultProperties = new()
    {
        Albedo = new(1, 1, 1),
        Opacity = 1,
    };

    public readonly MaterialTypeId MaterialTypeId = 0;

    public ref PBRProperties Properties => ref _pool!.GetRef(_handle)!;

    public bool Valid => _pool is not null && _handle.Valid;

    public uint Index => _handle.Index;

    internal MaterialProperties(
        MaterialTypeId materialTypeId,
        Pool<MaterialPropertyResource, PBRProperties> pool
    )
    {
        MaterialTypeId = materialTypeId;
        _pool = pool;
        _handle = _pool.Create(_defaultProperties);
        _eventBus.Publish(
            new MaterialPropsUpdatedEvent(MaterialTypeId, Index, MaterialPropertyOp.Create)
        );
    }

    public void NotifyUpdated()
    {
        if (Valid)
        {
            _eventBus.Publish(
                new MaterialPropsUpdatedEvent(MaterialTypeId, Index, MaterialPropertyOp.Update)
            );
        }
    }

    private MaterialProperties() { }

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                var index = Index;
                _pool?.Destroy(_handle);
                _eventBus.Publish(
                    new MaterialPropsUpdatedEvent(MaterialTypeId, index, MaterialPropertyOp.Destroy)
                );
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
