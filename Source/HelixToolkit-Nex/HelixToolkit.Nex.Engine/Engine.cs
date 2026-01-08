using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Engine;

public class Engine : Initializable
{
    private readonly RendererManager _rendererManager = new();
    private readonly Initializable[] _initializables;

    public Graphics.IContext Context { get; }

    public EngineConfig Config { get; private set; } = new EngineConfig();

    public override string Name => nameof(Engine);

    public Engine(Graphics.IContext context, EngineConfig? config = null)
    {
        Context = context;
        if (config != null)
        {
            Config = config;
        }
        _initializables = [_rendererManager];
    }

    protected override ResultCode OnInitializing()
    {
        for (var i = 0; i < _initializables.Length; ++i)
        {
            ResultCode ret = _initializables[i].Initialize();
            if (ret != ResultCode.Ok)
            {
                return ret;
            }
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        for (var i = _initializables.Length - 1; i >= 0; --i)
        {
            ResultCode ret = _initializables[i].Teardown();
            if (ret != ResultCode.Ok)
            {
                return ret;
            }
        }
        return ResultCode.Ok;
    }
}
