namespace HelixToolkit.Nex.Rendering;

public abstract class RenderNode : IDisposable
{
    protected Renderer? RenderManager { private set; get; }

    public abstract string Name { get; }
    public abstract Color4 DebugColor { get; }
    public virtual string Description { get; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the renderer is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public IContext? Context => RenderManager?.Context;

    private bool _isAttached = false;

    private readonly ITracer _tracer;

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

    public RenderNode()
    {
        _tracer = TracerFactory.GetTracer($"{nameof(RenderNode)}[{Name}]");
    }

    internal bool Setup(Renderer manager)
    {
        if (IsAttached)
        {
            if (RenderManager == manager)
            {
                return IsAttached;
            }
            throw new InvalidOperationException(
                "RenderNode is already attached to another RenderGraphManager."
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
        OnTeardown();
        RenderManager?.RemoveNode(this);
        RenderManager = null;
        _isAttached = false;
    }

    protected virtual void OnTeardown() { }

    public void Render(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        RenderPass pass,
        Framebuffer framebuf,
        Dependencies deps
    )
    {
        if (!IsAttached)
        {
            return;
        }
        using var scope = _tracer.BeginScope(nameof(Render));
        if (BeginRender(context, cmdBuffer, pass, framebuf, deps))
        {
            OnRender(context, cmdBuffer, deps);
            EndRender(context, cmdBuffer);
        }
    }

    protected virtual bool BeginRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        RenderPass pass,
        Framebuffer framebuf,
        Dependencies deps
    )
    {
        cmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        cmdBuffer.BeginRendering(pass, framebuf, deps);

        return true;
    }

    protected abstract void OnRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        Dependencies deps
    );

    protected virtual void EndRender(RenderContext context, ICommandBuffer cmdBuffer)
    {
        cmdBuffer.EndRendering();
        cmdBuffer.PopDebugGroupLabel();
    }

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
    // ~RenderNode()
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

public abstract class ComputeNode : RenderNode
{
    protected override bool BeginRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        RenderPass pass,
        Framebuffer framebuf,
        Dependencies deps
    )
    {
        // Compute nodes do not begin a render pass.
        cmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        return true;
    }

    protected override void EndRender(RenderContext context, ICommandBuffer cmdBuffer)
    {
        cmdBuffer.PopDebugGroupLabel();
        // Compute nodes do not end a render pass.
    }
}
