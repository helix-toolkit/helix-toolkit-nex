using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class PointDrawStreamRegistry(IContext context, World world)
    : Initializable,
        IDrawStreamRegistry<PointDraw>
{
    private readonly IContext _context = context;
    private readonly World _world = world;
    private readonly FastList<IDrawStream<PointDraw>?> _streams = [];
    private EntityCollection? _collections;
    private Components<PointDrawInfo> _pointComponents = world.GetComponents<PointDrawInfo>();
    private Components<Renderable> _renderables = world.GetComponents<Renderable>();

    public AllStreamsEnumerable<PointDraw> AllStreams => new(_streams);

    public override string Name => nameof(PointDrawStreamRegistry);

    public IDrawStream<PointDraw>? GetStream(DrawStreamType type, DrawStreamName name)
    {
        if (type != DrawStreamType.Point)
        {
            throw new ArgumentException(
                $"Invalid stream type {type} for point draw stream registry."
            );
        }
        return _streams[(int)name];
    }

    public IDrawStream<PointDraw>? GetStream(DrawStreamType type, DrawStreamVariants category)
    {
        return GetStream(type, category.GetStreamName());
    }

    public DrawStreamEnumerable<PointDraw> GetStreams(
        DrawStreamType type,
        DrawStreamVariants category
    )
    {
        if (type != DrawStreamType.Point)
        {
            throw new ArgumentException(
                $"Invalid stream type {type} for point draw stream registry."
            );
        }
        return new DrawStreamEnumerable<PointDraw>(_streams, category);
    }

    public DrawStreamEnumerable<PointDraw> GetStreams(DrawStreamType type) => new(_streams, null);

    protected override ResultCode OnInitializing()
    {
        _collections = _world
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<PointDrawInfo>()
            .Has<WorldTransform>()
            .Has<Renderable>()
            .Build();
        _collections.EntityAdded += OnEntityAdded;
        _collections.EntityRemoved += OnEntityRemoved;
        _collections.EntityChanged += OnEntityChanged;
        _streams.Resize((int)DrawStreamName.Count);
        for (var i = 0; i < (int)DrawStreamName.Count; ++i)
        {
            _streams[i] = new PointDrawStream(
                _context,
                _world,
                DrawStreamType.Point,
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

    private PointDrawStream? GetStreamInternal(DrawStreamType type, DrawStreamVariants category)
    {
        if (type != DrawStreamType.Point)
        {
            return null;
        }
        return GetStream(type, category) as PointDrawStream;
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
        ref var comp = ref _pointComponents[entity];
        var category = comp.Variants;
        return (DrawStreamType.Point, category);
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        var entity = _world.GetEntity(e.EntityId);
        ref var pointComp = ref _pointComponents[entity];

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
                    foreach (var s in _streams.AsValueEnumerable().Cast<PointDrawStream?>())
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
