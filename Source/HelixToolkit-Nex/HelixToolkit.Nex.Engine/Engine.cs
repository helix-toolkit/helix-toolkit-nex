using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Engine;

public class Engine : Initializable
{
    private readonly RendererManager _rendererManager;
    private readonly Initializable[] _initializables;

    public IContext Context { get; }

    public EngineConfig Config { get; }

    public override string Name => nameof(Engine);

    public Engine(EngineConfig config)
    {
        Config = config;
        Context = Config.Services.GetRequiredService<IContext>();
        _rendererManager = new RendererManager(Config.Services);
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
