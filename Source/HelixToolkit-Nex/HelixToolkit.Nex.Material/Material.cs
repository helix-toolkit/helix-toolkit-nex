namespace HelixToolkit.Nex.Material;

/// <summary>
/// Base abstraction for all materials used by the rendering engine.
/// Concrete material types should inherit from <see cref="Material{TProperties}"/> to expose typed properties.
/// </summary>
public class Material : IDisposable
{
    private bool _disposedValue;

    /// <summary>
    /// Optional per-material pipeline resource. Renderers may use this to cache a pipeline
    /// produced for this material (shaders, states, etc.). DefaultInvZ is <see cref="RenderPipelineResource.Null"/>.
    /// </summary>
    public RenderPipelineResource Pipeline { private set; get; } = RenderPipelineResource.Null;

    public virtual bool CreatePipeline(IContext context, in RenderPipelineDesc pipelineDesc)
    {
        Pipeline.Dispose();
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

/// <summary>
/// Generic material base that exposes strongly-typed material properties.
/// TProperties must have a public parameterless ctor so the factory / serializers can create it.
/// </summary>
/// <typeparam name="TProperties">The concrete MaterialProperties type for this material.</typeparam>
public abstract class Material<TProperties> : Material
    where TProperties : MaterialProperties, new()
{
    private TProperties? _properties;

    /// <summary>
    /// Lazily-created properties instance for this material.
    /// Renderers and editors should mutate values on this object; it raises property change notifications.
    /// </summary>
    public TProperties Properties => _properties ??= new TProperties();

    /// <inheritdoc/>
    public override string? DebugName => Properties.DebugName;
}

/// <summary>
/// Base class for material properties. Designed to be lightweight and engine-agnostic.
/// Contains change-tracking so renderers can react when material data is modified.
/// </summary>
public abstract class MaterialProperties : ObservableObject
{
    /// <summary>
    /// Optional debug name.
    /// </summary>
    public string? DebugName { get; set; }

    /// <summary>
    /// Creates a shallow copy of the properties. Override for deeper copy semantics when needed.
    /// </summary>
    public virtual MaterialProperties Clone()
    {
        return (MaterialProperties)MemberwiseClone();
    }
}
