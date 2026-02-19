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

    public IContext? Context => RenderManager?.Context;

    private bool _isAttached = false;

    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(Renderer));

    /// <summary>
    /// Gets a value indicating whether the object is currently attached to a render manager.
    /// </summary>
    public bool IsAttached => RenderManager != null && _isAttached;

    /// <summary>
    /// Gets the width of the rendered content.
    /// </summary>
    public int Width => RenderManager?.Width ?? 0;

    /// <summary>
    /// Gets the height of the rendered content.
    /// </summary>
    public int Height => RenderManager?.Height ?? 0;

    /// <summary>
    /// Gets the list of resource names that this renderer reads from.
    /// </summary>
    public virtual IEnumerable<string> GetInputs() => [];

    /// <summary>
    /// Gets the list of resource names that this renderer writes to.
    /// </summary>
    public virtual IEnumerable<string> GetOutputs() => [];

    internal bool Setup(RendererManager manager)
    {
        if (IsAttached)
        {
            if (RenderManager == manager)
            {
                return IsAttached;
            }
            throw new InvalidOperationException(
                "Renderer is already attached to another RendererManager."
            );
        }
        using var scope = _tracer.BeginScope($"Attaching renderer: {Name}");
        RenderManager = manager;
        _isAttached = OnSetup();
        return IsAttached;
    }

    protected abstract bool OnSetup();

    internal void TearDown()
    {
        if (!IsAttached)
        {
            return;
        }
        using var scope = _tracer.BeginScope($"Detaching renderer: {Name}");
        OnTearDown();
        RenderManager?.RemoveRenderer(this);
        RenderManager = null;
        _isAttached = false;
    }

    protected virtual void OnTearDown() { }

    public void Render(RenderContext context, ICommandBuffer cmdBuffer)
    {
        if (PreRender(context, cmdBuffer))
        {
            OnRender(context, cmdBuffer);
        }
    }

    protected virtual bool PreRender(RenderContext context, ICommandBuffer cmdBuffer)
    {
        return true;
    }

    protected abstract void OnRender(RenderContext context, ICommandBuffer cmdBuffer);

    public void Resize(int width, int height)
    {
        using var scope = _tracer.BeginScope($"Resizing renderer: {Name} to {width}x{height}");
        OnResize(width, height);
    }

    protected virtual void OnResize(int width, int height) { }

    #region IDisposable Support
    private bool _disposedValue;

    public bool IsDisposed => _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                TearDown();
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
