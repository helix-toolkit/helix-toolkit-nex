namespace HelixToolkit.Nex.Rendering;

public abstract class Renderer : IDisposable
{
    protected RendererManager? RenderManager { private set; get; }

    public abstract RenderStages Stage { get; }
    public abstract string Name { get; }
    public virtual string Description { get; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the renderer is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the object is currently attached to a render manager.
    /// </summary>
    public bool IsAttached => RenderManager != null;

    public void Attach(RendererManager manager)
    {
        if (IsAttached)
        {
            if (RenderManager == manager)
            {
                return;
            }
            throw new InvalidOperationException(
                "Renderer is already attached to another RendererManager."
            );
        }
        RenderManager = manager;
        OnAttach(manager);
    }

    protected virtual void OnAttach(RendererManager manager) { }

    public void Detach()
    {
        if (!IsAttached)
        {
            return;
        }
        OnDetach();
        RenderManager?.RemoveRenderer(this);
        RenderManager = null;
    }

    protected virtual void OnDetach() { }

    public void Render(RenderContext context)
    {
        if (PreRender(context))
        {
            OnRender(context);
        }
    }

    protected virtual bool PreRender(RenderContext context)
    {
        return true;
    }

    protected abstract void OnRender(RenderContext context);

    #region IDisposable Support
    private bool _disposedValue;

    public bool IsDisposed => _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Detach();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Renderer()
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
    #endregion
}
