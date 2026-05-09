using HelixToolkit.Nex.ECS.Events;

namespace HelixToolkit.Nex.Engine.Data;

public sealed class SceneState(IContext context, World world) : Initializable, IRenderData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(SceneState));
    private readonly IContext _context = context;

    private RingElementBuffer<GpuNodeInfo>? _buffer;

    public override string Name => nameof(SceneState);

    public World World { get; } = world;

    public bool NodeInfoDirty { private set; get; } = true;

    public bool SceneGraphDirty { private set; get; } = true;

    public BufferHandle Buffer => _buffer?.Buffer ?? BufferHandle.Null;

    public ulong GpuAddress => _buffer?.Buffer?.GpuAddress ?? 0;

    public uint Stride => NativeHelper.SizeOf<GpuNodeInfo>();

    public uint Count => (uint)(_buffer?.Count ?? 0);

    protected override ResultCode OnInitializing()
    {
        _buffer = new RingElementBuffer<GpuNodeInfo>(_context, (int)RenderSettings.MaxFrameInFlight, 1024);
        World.Register<ComponentChangedEvent<Transform>>(OnTransformChanged);
        World.Register<ComponentChangedEvent<WorldTransform>>(OnWorldTransformChanged);
        World.Register<ComponentChangedEvent<Parent>>(OnParentChanged);
        World.Register<ComponentChangedEvent<Renderable>>(OnRenderableChanged);
        World.Register<ComponentChangedEvent<NodeInfo>>(OnNodeInfoChanged);
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        World.Unregister<ComponentChangedEvent<Transform>>(OnTransformChanged);
        World.Unregister<ComponentChangedEvent<WorldTransform>>(OnWorldTransformChanged);
        World.Unregister<ComponentChangedEvent<Renderable>>(OnRenderableChanged);
        World.Unregister<ComponentChangedEvent<Parent>>(OnParentChanged);
        World.Unregister<ComponentChangedEvent<NodeInfo>>(OnNodeInfoChanged);
        Disposer.DisposeAndRemove(ref _buffer);
        return ResultCode.Ok;
    }

    private void OnTransformChanged(int worldId, ComponentChangedEvent<Transform> e)
    {
        NodeInfoDirty = true;
    }

    private void OnWorldTransformChanged(int worldId, ComponentChangedEvent<WorldTransform> e)
    {
        var entity = World.GetEntity(e.EntityId);
        if (entity.Has<Renderable>())
        {
            entity.Get<Renderable>().UpdateCounter = (int)RenderSettings.MaxFrameInFlight;
        }
    }

    private void OnParentChanged(int worldId, ComponentChangedEvent<Parent> e)
    {
        SceneGraphDirty = true;
    }

    private void OnRenderableChanged(int worldId, ComponentChangedEvent<Renderable> e)
    {
        SceneGraphDirty = true;
    }

    private void OnNodeInfoChanged(int worldId, ComponentChangedEvent<NodeInfo> e)
    {
        var entity = World.GetEntity(e.EntityId);
        if (entity.Has<Renderable>())
        {
            entity.Get<Renderable>().UpdateCounter = (int)RenderSettings.MaxFrameInFlight;
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
        else if (NodeInfoDirty)
        {
            PartialUpdate();
        }
        SceneGraphDirty = false;
        NodeInfoDirty = false;
        return true;
    }

    private void FullUpload()
    {
        var renderables = World.GetComponents<Renderable>();
        var nodeInfos = World.GetComponents<NodeInfo>();
        var worldTransforms = World.GetComponents<WorldTransform>();

        FastList<GpuNodeInfo> gpuNodeInfos = new(renderables.Count);
        foreach (var entityId in renderables.GetEntities())
        {
            var entity = World.GetEntity(entityId);
            ref var renderable = ref renderables[entity];
            renderable.GPUIndex = gpuNodeInfos.Count;
            renderable.UpdateCounter = 0;
            gpuNodeInfos.Add(new GpuNodeInfo
            {
                Enabled = nodeInfos[entity].Enabled ? 1u : 0u,
                WorldId = (uint)entity.WorldId,
                EntityId = (uint)entity.Id,
                Transform = worldTransforms[entity].Value,
                RenderMask = renderable.RenderMask
            });
        }
        _context.WaitAll(false);
        for (int i = 0; i < RenderSettings.MaxFrameInFlight; i++)
        {
            _buffer?.Advance();
            _buffer?.Upload(gpuNodeInfos);
        }

        World.Send(new SceneChangedEvents());
    }

    private void PartialUpdate()
    {
        _buffer?.Advance();
        var renderables = World.GetComponents<Renderable>();
        var nodeInfos = World.GetComponents<NodeInfo>();
        var worldTransforms = World.GetComponents<WorldTransform>();

        foreach (var entityId in renderables.GetEntities())
        {
            var entity = World.GetEntity(entityId);
            ref var renderable = ref renderables[entity];
            if (renderable.UpdateCounter > 0)
            {
                Debug.Assert(renderable.GPUIndex >= 0);
                renderable.UpdateCounter--;
                var info = new GpuNodeInfo
                {
                    Enabled = nodeInfos[entity].Enabled ? 1u : 0u,
                    WorldId = (uint)entity.WorldId,
                    EntityId = (uint)entity.Id,
                    Transform = worldTransforms[entity].Value,
                    RenderMask = renderable.RenderMask
                };
                _buffer?.WriteElement(ref info, renderable.GPUIndex);
            }
        }
    }
}
