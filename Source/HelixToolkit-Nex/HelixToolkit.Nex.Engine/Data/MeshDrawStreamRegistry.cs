using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class MeshDrawStreamRegistry(IContext context, World world)
    : Initializable,
        IDrawStreamRegistry
{
    private readonly IContext _context = context;
    private readonly World _world = world;
    private readonly FastList<IDrawStream> _streams = [];

    public IEnumerable<IDrawStream> AllStreams => _streams;

    public override string Name => nameof(MeshDrawStreamRegistry);

    public IDrawStream GetStream(DrawStreamName name)
    {
        return _streams[(int)name];
    }

    public IEnumerable<IDrawStream> GetStreams(DrawStreamCategory category)
    {
        foreach (var stream in _streams)
        {
            if (stream.Categories.HasFlag(category))
            {
                yield return stream;
            }
        }
    }

    protected override ResultCode OnInitializing()
    {
        _streams.Resize((int)DrawStreamName.Count);
        for (int i = 0; i < _streams.Count; ++i)
        {
            _streams[i] = new MeshDrawStream(_context, _world, (DrawStreamName)i);
            if (_streams[i].Initialize().CheckResult() != ResultCode.Ok)
            {
                return ResultCode.RuntimeError;
            }
        }
        return ResultCode.Ok;
    }

    public bool Update()
    {
        foreach (var stream in AllStreams)
        {
            if (!stream.Update())
            {
                return false;
            }
        }
        return true;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }
        _streams.Clear();
        return ResultCode.Ok;
    }
}
