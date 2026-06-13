using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class MeshDrawStreamRegistry(IContext context, World world)
    : Initializable,
        IDrawStreamRegistry<MeshDraw>
{
    private readonly IContext _context = context;
    private readonly World _world = world;
    private readonly FastList<FastList<IDrawStream<MeshDraw>?>> _streamsByType = new(
        (int)DrawStreamType.MeshStreamTypeCount
    );
    private EntityCollection? _collections;
    private Components<MeshDrawInfo> _meshComponents = world.GetComponents<MeshDrawInfo>();
    private Components<Renderable> _renderables = world.GetComponents<Renderable>();

    public AllStreamsEnumerable<MeshDraw> AllStreams => new(_streamsByType!);

    public override string Name => nameof(MeshDrawStreamRegistry);

    public IDrawStream<MeshDraw>? GetStream(DrawStreamType type, DrawStreamName name)
    {
        return _streamsByType[(int)type]![(int)name];
    }

    public IDrawStream<MeshDraw>? GetStream(DrawStreamType type, DrawStreamVariants category)
    {
        return GetStream(type, category.GetStreamName());
    }

    public DrawStreamEnumerable<MeshDraw> GetStreams(
        DrawStreamType type,
        DrawStreamVariants category
    )
    {
        return new DrawStreamEnumerable<MeshDraw>(_streamsByType[(int)type], category);
    }

    public DrawStreamEnumerable<MeshDraw> GetStreams(DrawStreamType type) =>
        new(_streamsByType[(int)type], null);

    protected override ResultCode OnInitializing()
    {
        _collections = _world
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<MeshDrawInfo>()
            .Has<WorldTransform>()
            .Has<Renderable>()
            .Build();
        _collections.EntityAdded += OnEntityAdded;
        _collections.EntityRemoved += OnEntityRemoved;
        _collections.EntityChanged += OnEntityChanged;

        _streamsByType.Resize((int)DrawStreamType.MeshStreamTypeCount);
        for (int i = 0; i < _streamsByType.Count; ++i)
        {
            _streamsByType[i] = new FastList<IDrawStream<MeshDraw>?>((int)DrawStreamName.Count);
            _streamsByType[i].Resize((int)DrawStreamName.Count);
            for (var j = 0; j < (int)DrawStreamName.Count; ++j)
            {
                _streamsByType[i][j] = new MeshDrawStream(
                    _context,
                    _world,
                    (DrawStreamType)i,
                    (DrawStreamName)j
                );
                var ret = _streamsByType[i][j]!.Initialize().CheckResult();
                if (ret != ResultCode.Ok)
                {
                    return ret;
                }
            }
        }
        return ResultCode.Ok;
    }

    public bool Update()
    {
        foreach (var streams in _streamsByType)
        {
            foreach (var stream in streams)
            {
                if (!stream!.Update())
                {
                    return false;
                }
            }
        }
        return true;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var streams in _streamsByType)
        {
            foreach (var stream in streams)
            {
                stream?.Dispose();
            }
            streams.Clear();
        }
        _streamsByType.Clear();
        return ResultCode.Ok;
    }

    private MeshDrawStream? GetStreamInternal(DrawStreamType type, DrawStreamVariants category)
    {
        return GetStream(type, category) is MeshDrawStream mesh ? mesh : null;
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
        ref var comp = ref _meshComponents[entity];
        var category = comp.Variants;
        var type = DrawStreamType.Opaque;
        if (entity.Has<TransparentComponent>())
        {
            type = DrawStreamType.Transparent;
        }
        else if (entity.Has<AlphaMaskComponent>())
        {
            type = DrawStreamType.AlphaMask;
        }
        return (type, category);
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        var entity = _world.GetEntity(e.EntityId);
        ref var meshComp = ref _meshComponents[entity];

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
                    foreach (var streams in _streamsByType)
                    {
                        foreach (var s in streams.Cast<MeshDrawStream?>())
                        {
                            if (s?.Has(entity) == true)
                            {
                                s.EntityRemoved(entity);
                                break;
                            }
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
