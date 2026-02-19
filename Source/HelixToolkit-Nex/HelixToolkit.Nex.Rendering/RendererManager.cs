using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.Rendering;

public class RendererManager(IServiceProvider serviceProvider) : Initializable
{
    private static readonly ILogger _logger = LogManager.Create<RendererManager>();
    private readonly Dictionary<RenderStages, List<Renderer>> _renderers = [];
    public IServiceProvider Services { get; } = serviceProvider;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public readonly IContext Context = serviceProvider.GetRequiredService<IContext>();
    public readonly IShaderRepository ShaderRepository =
        serviceProvider.GetRequiredService<IShaderRepository>();

    public override string Name => nameof(RendererManager);

    private RenderGraph _renderGraph = new();

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
            renderer.Setup(this);
            _renderGraph.AddPass(renderer);
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
                _renderGraph.RemovePass(renderer);
                renderer.TearDown();
            }
        }
    }

    public void CompileGraph()
    {
        _renderGraph.Compile();
    }

    public void Render(RenderContext context, ICommandBuffer commandBuffer)
    {
        _renderGraph.Execute(context, commandBuffer);
    }

    public void Resize(int width, int height)
    {
        if (Width == width && Height == height)
        {
            return; // No change in size
        }
        Width = width;
        Height = height;
        foreach (var kvp in _renderers)
        {
            foreach (var renderer in kvp.Value)
            {
                renderer.Resize(width, height);
            }
        }
    }

    public void Clear()
    {
        _renderGraph = new();
        foreach (var kvp in _renderers)
        {
            foreach (var renderer in kvp.Value.ToArray())
            {
                renderer.TearDown();
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
