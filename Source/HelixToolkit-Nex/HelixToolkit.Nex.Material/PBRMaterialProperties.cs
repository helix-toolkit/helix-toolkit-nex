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

public sealed class PBRMaterialProperties : IDisposable
{
    private static readonly EventBus _eventBus = EventBus.Instance;
    private readonly Pool<MaterialPropertyResource, PBRProperties>? _pool;
    private readonly Handle<MaterialPropertyResource> _handle =
        Handle<MaterialPropertyResource>.Null;
    internal static readonly PBRProperties DefaultProperties = new()
    {
        Albedo = new(1, 1, 1),
        Opacity = 1,
    };

    public readonly MaterialTypeId MaterialTypeId = 0;

    public ref PBRProperties Properties => ref _pool!.GetRef(_handle)!;

    public bool Valid => _pool is not null && _handle.Valid;

    public uint Index => _handle.Index;

    public Color Albedo
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Albedo == newValue)
            {
                return;
            }
            Properties.Albedo = newValue;
            NotifyUpdated();
        }
        get => new(Properties.Albedo);
    }

    public float Opacity
    {
        set
        {
            if (Properties.Opacity == value)
            {
                return;
            }
            Properties.Opacity = value;
            NotifyUpdated();
        }
        get => Properties.Opacity;
    }

    public float Metallic
    {
        set
        {
            if (Properties.Metallic == value)
            {
                return;
            }
            Properties.Metallic = value;
            NotifyUpdated();
        }
        get => Properties.Metallic;
    }
    public Color Emissive
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Emissive == newValue)
            {
                return;
            }
            Properties.Emissive = value.ToVector3();
            NotifyUpdated();
        }
        get => new(Properties.Emissive);
    }
    public float Roughness
    {
        set
        {
            if (Properties.Roughness == value)
            {
                return;
            }
            Properties.Roughness = value;
            NotifyUpdated();
        }
        get => Properties.Roughness;
    }
    public Color Ambient
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Ambient == newValue)
            {
                return;
            }
            Properties.Ambient = value.ToVector3();
            NotifyUpdated();
        }
        get => new(Properties.Ambient);
    }
    public float Ao
    {
        set
        {
            if (Properties.Ao == value)
            {
                return;
            }
            Properties.Ao = value;
            NotifyUpdated();
        }
        get => Properties.Ao;
    }

    public float VertexColorMix
    {
        set
        {
            if (Properties.VertexColorMix == value)
            {
                return;
            }
            Properties.VertexColorMix = value;
            NotifyUpdated();
        }
        get => Properties.VertexColorMix;
    }

    public uint AlbedoTexIndex
    {
        set
        {
            if (Properties.AlbedoTexIndex == value)
            {
                return;
            }
            Properties.AlbedoTexIndex = value;
            NotifyUpdated();
        }
        get => Properties.AlbedoTexIndex;
    }

    public uint NormalTexIndex
    {
        set
        {
            if (Properties.NormalTexIndex == value)
            {
                return;
            }
            Properties.NormalTexIndex = value;
            NotifyUpdated();
        }
        get => Properties.NormalTexIndex;
    }

    public uint MetallicRoughnessTexIndex
    {
        set
        {
            if (Properties.MetallicRoughnessTexIndex == value)
            {
                return;
            }
            Properties.MetallicRoughnessTexIndex = value;
            NotifyUpdated();
        }
        get => Properties.MetallicRoughnessTexIndex;
    }

    public uint SamplerIndex
    {
        set
        {
            if (Properties.SamplerIndex == value)
            {
                return;
            }
            Properties.SamplerIndex = value;
            NotifyUpdated();
        }
        get => Properties.SamplerIndex;
    }

    internal PBRMaterialProperties(
        MaterialTypeId materialTypeId,
        ref PBRProperties properties,
        Pool<MaterialPropertyResource, PBRProperties> pool
    )
    {
        MaterialTypeId = materialTypeId;
        _pool = pool;
        _handle = _pool.Create(properties);
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

    private PBRMaterialProperties() { }

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

    public static readonly PBRMaterialProperties Null = new();
}
