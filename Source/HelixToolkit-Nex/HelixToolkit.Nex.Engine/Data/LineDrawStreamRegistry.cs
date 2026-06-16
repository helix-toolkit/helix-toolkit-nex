using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class LineDrawStreamRegistry(IContext context, World world)
    : Initializable,
        IDrawStreamRegistry<LineDraw>
{
    private readonly IContext _context = context;
    private readonly World _world = world;
    private readonly FastList<IDrawStream<LineDraw>?> _streams = [];
    private EntityCollection? _collections;
    private Components<LineDrawInfo> _lineComponents = world.GetComponents<LineDrawInfo>();
    private Components<Renderable> _renderables = world.GetComponents<Renderable>();

    public AllStreamsEnumerable<LineDraw> AllStreams => new(_streams);

    public override string Name => nameof(LineDrawStreamRegistry);

    public IDrawStream<LineDraw>? GetStream(DrawStreamType type, DrawStreamName name)
    {
        if (type != DrawStreamType.Line)
        {
            throw new ArgumentException(
                $"Invalid stream type {type} for line draw stream registry."
            );
        }
        return _streams[(int)name];
    }

    public IDrawStream<LineDraw>? GetStream(DrawStreamType type, DrawStreamVariants category)
    {
        return GetStream(type, category.GetStreamName());
    }

    public DrawStreamEnumerable<LineDraw> GetStreams(
        DrawStreamType type,
        DrawStreamVariants category
    )
    {
        if (type != DrawStreamType.Line)
        {
            throw new ArgumentException(
                $"Invalid stream type {type} for line draw stream registry."
            );
        }
        return new DrawStreamEnumerable<LineDraw>(_streams, category);
    }

    public DrawStreamEnumerable<LineDraw> GetStreams(DrawStreamType type) =>
        new(_streams, null);

    protected override ResultCode OnInitializing()
    {
        _collections = _world
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<LineDrawInfo>()
            .Has<WorldTransform>()
            .Has<Renderable>()
            .Build();
        _collections.EntityAdded += OnEntityAdded;
        _collections.EntityRemoved += OnEntityRemoved;
        _collections.EntityChanged += OnEntityChanged;
        _streams.Resize((int)DrawStreamName.Count);
        for (var i = 0; i < (int)DrawStreamName.Count; ++i)
        {
            _streams[i] = new LineDrawStream(
                _context,
                _world,
                DrawStreamType.Line,
                (DrawStreamName)i
            );
            var ret = _streams[i]!.Initialize().CheckResult();
            if (ret != ResultCode.Ok)
            {
                return ret;
            }
        }
        return ResultCode.Ok;
    }

    public bool Update()
    {
        foreach (var stream in _streams.AsValueEnumerable())
        {
            if (stream == null)
            {
                continue;
            }
            if (!stream.Update())
            {
                return false;
            }
        }
        return true;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var stream in _streams.AsValueEnumerable())
        {
            stream?.Dispose();
        }
        _streams.Clear();
        return ResultCode.Ok;
    }

    private LineDrawStream? GetStreamInternal(DrawStreamType type, DrawStreamVariants category)
    {
        if (type != DrawStreamType.Line)
        {
            return null;
        }
        return GetStream(type, category) as LineDrawStream;
    }

    private void OnEntityAdded(object? sender, int entityId)
    {
        var entity = _world.GetEntity(entityId);
        var (type, category) = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(type, category);
        stream!.EntityAdded(entity);
    }

    private void OnEntityRemoved(object? sender, int entityId)
    {
        var entity = _world.GetEntity(entityId);
        var (type, category) = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(type, category);
        stream!.EntityRemoved(entity);
    }

    private (DrawStreamType, DrawStreamVariants) GetCategoryFromEntity(Entity entity)
    {
        ref var comp = ref _lineComponents[entity];
        var category = comp.Variants;
        return (DrawStreamType.Line, category);
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        var entity = _world.GetEntity(e.EntityId);
        ref var lineComp = ref _lineComponents[entity];

        var (type, category) = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(type, category);
        if (!stream!.Has(entity))
        {
            ref var renderable = ref _renderables[entity];
            var oldCategory = (DrawStreamVariants)renderable.DrawVariants;
            var oldType = (DrawStreamType)renderable.DrawType;
            if (oldType != type || oldCategory != category)
            {
                var oldStream = GetStreamInternal(oldType, oldCategory);
                if (oldStream is not null && oldStream.Has(entity))
                {
                    oldStream.EntityRemoved(entity);
                }
                else
                {
                    // In case the entity was not added to any stream before (e.g., it was missing some components), we need to remove it from all streams just in case.
                    foreach (var s in _streams.AsValueEnumerable().Cast<LineDrawStream?>())
                    {
                        if (s?.Has(entity) == true)
                        {
                            s.EntityRemoved(entity);
                            break;
                        }
                    }
                }
            }
            stream.EntityAdded(entity);
        }
        else
        {
            stream.EntityChanged(entity, in e);
        }
    }
}
