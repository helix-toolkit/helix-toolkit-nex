using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Material;

public readonly struct MaterialTypeId(uint id) : IComparable<MaterialTypeId>
{
    public uint Id { get; } = id;

    public int CompareTo(MaterialTypeId other)
    {
        return Id.CompareTo(other.Id);
    }

    public static implicit operator uint(MaterialTypeId id) => id.Id;

    public static implicit operator MaterialTypeId(uint id) => new(id);

    public static implicit operator MaterialTypeId(int id) => new((uint)id);

    public static implicit operator MaterialTypeId(PBRShadingMode mode) => new((uint)mode);
}

/// <summary>
/// Base abstraction for all materials used by the rendering engine.
/// Concrete material types should inherit from <see cref="Material{TProperties}"/> to expose typed properties.
/// </summary>
public class Material : IDisposable
{
    public readonly MaterialTypeId MaterialId;
    private bool _disposedValue;

    /// <summary>
    /// Optional per-material pipeline resource. Renderers may use this to cache a pipeline
    /// produced for this material (shaders, states, etc.). DefaultReversedZ is <see cref="RenderPipelineResource.Null"/>.
    /// </summary>
    public RenderPipelineResource Pipeline { private set; get; } = RenderPipelineResource.Null;

    public Material(string name)
    {
        MaterialId =
            MaterialTypeRegistry.GetTypeId(name)
            ?? throw new ArgumentException(name + " is not a registered material type.");
    }

    public Material()
    {
        MaterialId = 0; // Invalid material type
    }

    public virtual bool CreatePipeline(IContext context, in RenderPipelineDesc pipelineDesc)
    {
        Pipeline.Dispose();
        pipelineDesc.WriteSpecInfo(0, MaterialId);
        // Create the pipeline
        Pipeline = context.CreateRenderPipeline(pipelineDesc);

        return Pipeline.Valid;
    }

    /// <summary>
    /// Optional friendly name useful for debugging or UI.
    /// Delegates to the underlying properties debug name when available.
    /// </summary>
    public virtual string? DebugName => null;

    protected virtual void OnDisposing() { }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                OnDisposing();
                Pipeline.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Material()
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
