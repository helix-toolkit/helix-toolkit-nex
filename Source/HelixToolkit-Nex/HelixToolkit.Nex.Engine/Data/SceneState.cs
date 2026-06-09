using HelixToolkit.Nex.ECS.Events;

namespace HelixToolkit.Nex.Engine.Data;

public sealed class SceneState(IContext context, World world) : Initializable, IRenderData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(SceneState));
    private readonly IContext _context = context;

    private RingElementBuffer<GpuNodeInfo>? _buffer;
    private Components<Renderable> _renderables;
    private Components<NodeInfo> _nodeInfos;
    private Components<WorldTransform> _worldTransforms;
    private readonly FastList<Subscription> _subscriptions = [];

    public override string Name => nameof(SceneState);

    public World World { get; } = world;

    public bool NodeInfoDirty { private set; get; } = true;

    public bool SceneGraphDirty { private set; get; } = true;

    public bool TransformUpdated { private set; get; } = true;

    public BufferHandle Buffer => _buffer?.Buffer ?? BufferHandle.Null;

    public ulong GpuAddress => _buffer?.Buffer?.GpuAddress ?? 0;

    public uint Stride => NativeHelper.SizeOf<GpuNodeInfo>();

    public uint Count => (uint)(_buffer?.Count ?? 0);

    protected override ResultCode OnInitializing()
    {
        _buffer = new RingElementBuffer<GpuNodeInfo>(_context, (int)GraphicsSettings.MaxFrameInFlight, 1024);
        _renderables = World.GetComponents<Renderable>();
        _worldTransforms = World.GetComponents<WorldTransform>();
        _nodeInfos = World.GetComponents<NodeInfo>();
        _subscriptions.Add(World.Register<ComponentChangedEvent<Transform>>(OnTransformChanged));
        _subscriptions.Add(World.Register<ComponentChangedEvent<WorldTransform>>(OnWorldTransformChanged));
        _subscriptions.Add(World.Register<ComponentChangedEvent<Parent>>(OnParentChanged));
        _subscriptions.Add(World.Register<ComponentChangedEvent<Renderable>>(OnRenderableChanged));
        _subscriptions.Add(World.Register<ComponentChangedEvent<NodeInfo>>(OnNodeInfoChanged));
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
        Disposer.DisposeAndRemove(ref _buffer);
        return ResultCode.Ok;
    }

    private void OnTransformChanged(World _, ComponentChangedEvent<Transform> e)
    {
        NodeInfoDirty = true;
    }

    private void OnWorldTransformChanged(World _, ComponentChangedEvent<WorldTransform> e)
    {
        var entity = World.GetEntity(e.EntityId);
        if (entity.Has<Renderable>())
        {
            _renderables[entity].UpdateCounter = (int)GraphicsSettings.MaxFrameInFlight;
            TransformUpdated = true;
        }
    }

    private void OnParentChanged(World _, ComponentChangedEvent<Parent> e)
    {
        SceneGraphDirty = true;
    }

    private void OnRenderableChanged(World _, ComponentChangedEvent<Renderable> e)
    {
        SceneGraphDirty = true;
    }

    private void OnNodeInfoChanged(World _, ComponentChangedEvent<NodeInfo> e)
    {
        var entity = World.GetEntity(e.EntityId);
        if (entity.Has<Renderable>())
        {
            _renderables[entity].UpdateCounter = (int)GraphicsSettings.MaxFrameInFlight;
            NodeInfoDirty = true;
        }
    }

    public bool Update()
    {
        if (SceneGraphDirty)
        {
            using var scope = _tracer.BeginScope("SortSceneNodes");
            World.SortSceneNodes();
            NodeInfoDirty = true;
        }
        if (NodeInfoDirty)
        {
            using var scope = _tracer.BeginScope("UpdateTransforms");
            World.UpdateTransforms();
        }

        if (SceneGraphDirty)
        {
            FullUpload();
        }
        else if (NodeInfoDirty || TransformUpdated)
        {
            PartialUpdate();
        }
        SceneGraphDirty = false;
        NodeInfoDirty = false;
        TransformUpdated = false;
        return true;
    }

    private void FullUpload()
    {
        FastList<GpuNodeInfo> gpuNodeInfos = new(_renderables.Count);
        foreach (var entityId in _renderables.GetEntities())
        {
            var entity = World.GetEntity(entityId);
            ref var renderable = ref _renderables[entity];
            renderable.GPUIndex = gpuNodeInfos.Count;
            renderable.UpdateCounter = 0;
            gpuNodeInfos.Add(new GpuNodeInfo
            {
                Enabled = _nodeInfos[entity].Enabled ? 1u : 0u,
                WorldId = (uint)entity.WorldId,
                EntityId = (uint)entity.Id,
                Transform = _worldTransforms[entity].Value,
                RenderMask = renderable.RenderMask
            });
        }
        _context.WaitAll(false);
        for (int i = 0; i < GraphicsSettings.MaxFrameInFlight; i++)
        {
            _buffer?.Upload(gpuNodeInfos);
            _buffer?.Advance();
        }

        World.Send(new SceneChangedEvents());
    }

    private void PartialUpdate()
    {
        _buffer?.Advance();

        foreach (var entityId in _renderables.GetEntities())
        {
            var entity = World.GetEntity(entityId);
            ref var renderable = ref _renderables[entity];
            if (renderable.UpdateCounter > 0)
            {
                Debug.Assert(renderable.GPUIndex >= 0);
                renderable.UpdateCounter--;
                var info = new GpuNodeInfo
                {
                    Enabled = _nodeInfos[entity].Enabled ? 1u : 0u,
                    WorldId = (uint)entity.WorldId,
                    EntityId = (uint)entity.Id,
                    Transform = _worldTransforms[entity].Value,
                    RenderMask = renderable.RenderMask
                };
                Debug.Assert(renderable.GPUIndex < _buffer!.Capacity);
                _buffer?.WriteElement(ref info, renderable.GPUIndex);
            }
        }
    }
}
