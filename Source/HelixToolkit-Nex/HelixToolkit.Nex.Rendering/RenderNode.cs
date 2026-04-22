namespace HelixToolkit.Nex.Rendering;

public readonly record struct RenderResources(
    RenderContext Context,
    ICommandBuffer CmdBuffer,
    RenderPass Pass,
    Framebuffer Framebuf,
    Dependencies Deps,
    Dictionary<string, TextureHandle> Textures,
    Dictionary<string, BufferHandle> Buffers
);

public abstract class RenderNode : IDisposable
{
    protected Renderer? Renderer { private set; get; }

    public abstract string Name { get; }
    public abstract Color4 DebugColor { get; }
    public virtual string Description { get; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether this render node is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public IContext? Context => Renderer?.Context;

    public IResourceManager? ResourceManager => Renderer?.ResourceManager;

    private bool _isAttached = false;

    private ITracer? _tracer;

    /// <summary>
    /// Gets a value indicating whether the render node is currently attached to a renderer.
    /// </summary>
    public bool IsAttached => Renderer != null && _isAttached;

    internal bool Setup(Renderer renderer)
    {
        if (IsAttached)
        {
            if (Renderer == renderer)
            {
                return IsAttached;
            }
            throw new InvalidOperationException(
                "RenderNode is already attached to another Renderer."
            );
        }
        _tracer = TracerFactory.GetTracer($"{nameof(RenderNode)}[{Name}]");
        using var scope = _tracer.BeginScope($"Attaching renderer: {Name}");
        Renderer = renderer;
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
        // Mark as detached before calling into teardown logic to prevent re-entrant calls.
        _isAttached = false;
        using var scope = _tracer?.BeginScope($"Detaching renderer: {Name}");
        OnTeardown();
        Renderer?.RemoveNode(this);
        Renderer = null;
    }

    protected virtual void OnTeardown() { }

    public void Render(in RenderResources res)
    {
        if (!IsAttached)
        {
            return;
        }
        using var scope = _tracer?.BeginScope(nameof(Render));
        res.CmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        if (BeginRender(in res))
        {
            OnRender(in res);
            EndRender(in res);
        }
        res.CmdBuffer.PopDebugGroupLabel();
    }

    protected virtual bool BeginRender(in RenderResources res)
    {
        res.CmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        return true;
    }

    protected abstract void OnRender(in RenderResources res);

    protected virtual void EndRender(in RenderResources res)
    {
        res.CmdBuffer.EndRendering();
    }

    public abstract void AddToGraph(RenderGraph graph);

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
    protected override bool BeginRender(in RenderResources res)
    {
        // Compute nodes do not begin a render pass.
        res.CmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        return true;
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.PopDebugGroupLabel();
        // Compute nodes do not end a render pass.
    }
}
