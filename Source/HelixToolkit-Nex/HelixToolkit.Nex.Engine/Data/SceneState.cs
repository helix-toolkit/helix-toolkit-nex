using HelixToolkit.Nex.ECS.Events;

namespace HelixToolkit.Nex.Engine.Data;

public sealed class SceneState(World world) : Initializable
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(SceneState));
    public override string Name => nameof(SceneState);

    public World World { get; } = world;

    public bool TransformDirty { private set; get; } = true;

    public bool SceneGraphDirty { private set; get; } = true;

    public void Update()
    {
        if (SceneGraphDirty)
        {
            World.SortSceneNodes();
            SceneGraphDirty = false;
        }
        if (TransformDirty)
        {
            World.UpdateTransforms();
            TransformDirty = false;
        }
    }

    protected override ResultCode OnInitializing()
    {
        World.Register<ComponentChangedEvent<Transform>>(OnTransformChanged);
        World.Register<ComponentChangedEvent<Parent>>(OnParentChanged);
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        World.Unregister<ComponentChangedEvent<Transform>>(OnTransformChanged);
        return ResultCode.Ok;
    }

    private void OnTransformChanged(int worldId, ComponentChangedEvent<Transform> e)
    {
        TransformDirty = true;
    }

    private void OnParentChanged(int worldId, ComponentChangedEvent<Parent> e)
    {
        SceneGraphDirty = true;
    }
}
