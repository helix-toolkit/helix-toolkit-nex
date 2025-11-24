namespace HelixToolkit.Nex.Rendering;

public class RendererManager : Initializable
{
    private static readonly ILogger _logger = LogManager.Create<RendererManager>();
    private readonly Dictionary<RenderStages, List<Renderer>> _renderers = [];

    public override string Name => nameof(RendererManager);
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
            _logger.LogError(
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

    protected override ResultCode OnInitializing()
    {
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Clear();
        return ResultCode.Ok;
    }
}
