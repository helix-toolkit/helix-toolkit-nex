using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class MeshDrawStreamRegistry(IContext context, World world)
    : Initializable,
        IDrawStreamRegistry
{
    private readonly IContext _context = context;
    private readonly World _world = world;
    private readonly FastList<IDrawStream> _streams = [];
    private readonly FastList<MeshDrawStream> _meshStreams = [];
    private EntityCollection? _collections;
    private Components<MeshComponent> _meshComponents = world.GetComponents<MeshComponent>();
    private Components<Renderable> _renderables = world.GetComponents<Renderable>();

    public IEnumerable<IDrawStream> AllStreams => _streams;

    public override string Name => nameof(MeshDrawStreamRegistry);

    public IDrawStream GetStream(DrawStreamName name)
    {
        return _streams[(int)name];
    }

    public IDrawStream GetStream(DrawStreamCategory category)
    {
        return GetStream(category.GetStreamName());
    }

    public IEnumerable<IDrawStream> GetStreams(DrawStreamCategory category)
    {
        foreach (var stream in _streams)
        {
            if (stream.Categories.HasAllFlags(category))
                yield return stream;
        }
    }

    public MeshDrawStreamEnumerable GetStreamsCore(DrawStreamCategory category) =>
        new(_streams, category);

    protected override ResultCode OnInitializing()
    {
        _streams.Resize((int)DrawStreamName.Count);
        _meshStreams.Resize((int)DrawStreamName.Count);
        _collections = _world
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<MeshComponent>()
            .Has<WorldTransform>()
            .Has<Renderable>()
            .Build();
        _collections.EntityAdded += OnEntityAdded;
        _collections.EntityRemoved += OnEntityRemoved;
        _collections.EntityChanged += OnEntityChanged;
        for (int i = 0; i < _streams.Count; ++i)
        {
            _streams[i] = _meshStreams[i] = new MeshDrawStream(_context, _world, (DrawStreamName)i);
            if (_streams[i].Initialize().CheckResult() != ResultCode.Ok)
            {
                return ResultCode.RuntimeError;
            }
        }
        _world.Register<SceneChangedEvents>(OnSceneChanged);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        foreach (var stream in _meshStreams)
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
        _meshStreams.Clear();
        return ResultCode.Ok;
    }

    private MeshDrawStream? GetStreamInternal(DrawStreamCategory category)
    {
        var idx = (int)category.GetStreamName();
        return (idx >= 0 && idx < _meshStreams.Count) ? _meshStreams[idx] : null;
    }

    private void OnEntityAdded(object? sender, int entityId)
    {
        var entity = _world.GetEntity(entityId);
        var category = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(category);
        stream!.EntityAdded(entity);
    }

    private void OnEntityRemoved(object? sender, int entityId)
    {
        var entity = _world.GetEntity(entityId);
        var category = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(category);
        stream!.EntityRemoved(entity);
    }

    private DrawStreamCategory GetCategoryFromEntity(Entity entity)
    {
        ref var comp = ref _meshComponents[entity];
        var category = comp.Category;
        if (entity.Has<TransparentComponent>())
        {
            category |= DrawStreamCategory.Transparent;
        }
        else
        {
            category |= DrawStreamCategory.Opaque;
        }
        return category;
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        var entity = _world.GetEntity(e.EntityId);
        ref var meshComp = ref _meshComponents[entity];

        var newCategory = GetCategoryFromEntity(entity);
        var stream = GetStreamInternal(newCategory);
        if (!stream!.Has(entity))
        {
            ref var renderable = ref _renderables[entity];
            var oldCategory = (DrawStreamCategory)renderable.DrawCategory;
            if (oldCategory != newCategory)
            {
                var oldStream = GetStreamInternal(oldCategory);
                if (oldStream is not null && oldStream.Has(entity))
                {
                    oldStream.EntityRemoved(entity);
                }
                else
                {
                    // In case the entity was not added to any stream before (e.g., it was missing some components), we need to remove it from all streams just in case.
                    foreach (var s in _meshStreams)
                    {
                        if (s.Has(entity))
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

    private void OnSceneChanged(int worldId, SceneChangedEvents message) { }
}
