namespace HelixToolkit.Nex.Rendering;

public class RendererManager : IDisposable
{
    private static readonly ILogger Logger = LogManager.Create<RendererManager>();
    private readonly Dictionary<RenderStages, List<Renderer>> _renderers = [];

    public virtual RenderStages[] RenderOrder =>
        [
            RenderStages.Begin,
            RenderStages.Opaque,
            RenderStages.Transparent,
            RenderStages.PostEffect,
            RenderStages.Overlay,
            RenderStages.UI,
            RenderStages.Composition,
            RenderStages.End,
        ];

    public bool AddRenderer(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var stage = renderer.Stage;
        if (!_renderers.TryGetValue(stage, out var list))
        {
            list = [];
            _renderers[stage] = list;
        }
        if (list.Contains(renderer))
        {
            return false;
        }
        try
        {
            renderer.Attach(this);
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "Failed to attach renderer '{RendererName}' to renderer manager.",
                renderer.Name
            );
            return false;
        }
        list.Add(renderer);
        return true;
    }

    public void RemoveRenderer(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var stage = renderer.Stage;
        if (_renderers.TryGetValue(stage, out var list))
        {
            if (list.Remove(renderer))
            {
                renderer.Detach();
            }
        }
    }

    public void Render(RenderContext context)
    {
        foreach (var stage in RenderOrder)
        {
            RenderStage(stage, context);
        }
    }

    public void RenderStage(RenderStages stage, RenderContext context)
    {
        if (_renderers.TryGetValue(stage, out var list))
        {
            foreach (var renderer in list)
            {
                if (renderer.Enabled)
                {
                    renderer.Render(context);
                }
            }
        }
    }

    public void Clear()
    {
        foreach (var kvp in _renderers)
        {
            foreach (var renderer in kvp.Value.ToArray())
            {
                renderer.Detach();
            }
        }
        _renderers.Clear();
    }

    #region IDisposable Support

    private bool _disposedValue;

    public bool IsDisposed => _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Clear();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~RendererManager()
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
