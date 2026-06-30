// ResultCode refers to the single graphics enum HelixToolkit.Nex.ResultCode, resolved from the
// enclosing HelixToolkit.Nex namespace.
namespace HelixToolkit.Nex.Scene;
/// <summary>
/// A single deferred scene operation recorded into a <see cref="SceneCommandBuffer"/>.
/// </summary>
/// <remarks>
/// Concrete commands carry their recorded payload and know how to apply themselves during
/// flush, operating over a materialized-node table. <see cref="SceneCommandBuffer.Flush"/>
/// applies the recorded commands in order and detects failures either through the returned
/// <see cref="ResultCode"/> or through an <see cref="InvalidOperationException"/> thrown by
/// <see cref="Node.AddChild"/> when a child already has a parent.
/// </remarks>
internal interface ISceneCommand
{
    /// <summary>
    /// Applies this command using the materialized-node table.
    /// </summary>
    /// <param name="world">The target world, used to construct nodes during flush.</param>
    /// <param name="nodes">The materialized-node table indexed by deferred-node index.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success; otherwise a failing code.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown by an add-child command when the child node already has a parent. The flush
    /// catches this, stops, and reports the offending command.
    /// </exception>
    ResultCode Apply(World world, Node[] nodes);

    /// <summary>
    /// Returns a human-readable description of this command, used in failure messages.
    /// </summary>
    string Describe();
}

/// <summary>
/// Records the creation of a <see cref="Node"/>. During flush the node is materialized with
/// <c>new Node(world)</c>, which sets the <c>NodeInfo</c>, <c>Transform</c>,
/// <c>WorldTransform</c>, and <c>Parent</c> components.
/// </summary>
internal sealed class CreateNodeCommand(int index) : ISceneCommand
{
    /// <summary>
    /// The slot in the materialized-node table that receives the new node.
    /// </summary>
    public int Index { get; } = index;

    public ResultCode Apply(World world, Node[] nodes)
    {
        nodes[Index] = new Node(world);
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(CreateNodeCommand)}(index: {Index})";
}

/// <summary>
/// Records a parent-child relationship to be wired via <see cref="Node.AddChild"/> during flush.
/// </summary>
internal sealed class AddChildCommand(int parentIndex, int childIndex) : ISceneCommand
{
    /// <summary>
    /// The materialized-node table slot of the parent node.
    /// </summary>
    public int ParentIndex { get; } = parentIndex;

    /// <summary>
    /// The materialized-node table slot of the child node.
    /// </summary>
    public int ChildIndex { get; } = childIndex;

    /// <remarks>
    /// <see cref="Node.AddChild"/> throws <see cref="InvalidOperationException"/> when the child
    /// already has a parent. The exception is intentionally allowed to propagate so the flush can
    /// catch it, stop, and identify this command as the point of failure.
    /// </remarks>
    public ResultCode Apply(World world, Node[] nodes)
    {
        nodes[ParentIndex].AddChild(nodes[ChildIndex]);
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(AddChildCommand)}(parent: {ParentIndex}, child: {ChildIndex})";
}

/// <summary>
/// Records a node name to be applied to the materialized <see cref="Node.Name"/> during flush.
/// </summary>
internal sealed class SetNameCommand(int index, string name) : ISceneCommand
{
    /// <summary>
    /// The materialized-node table slot of the target node.
    /// </summary>
    public int Index { get; } = index;

    /// <summary>
    /// The recorded name to apply.
    /// </summary>
    public string Name { get; } = name;

    public ResultCode Apply(World world, Node[] nodes)
    {
        nodes[Index].Name = Name;
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(SetNameCommand)}(index: {Index}, name: {Name})";
}

/// <summary>
/// Records a local <see cref="Transform"/> to be applied to the materialized node during flush.
/// </summary>
internal sealed class SetLocalTransformCommand : ISceneCommand
{
    /// <summary>
    /// The materialized-node table slot of the target node.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The recorded local transform (a copy of the value captured at record time).
    /// </summary>
    public Transform Transform { get; }

    public SetLocalTransformCommand(int index, in Transform transform)
    {
        Index = index;
        Transform = transform;
    }

    public ResultCode Apply(World world, Node[] nodes)
    {
        nodes[Index].Transform = Transform;
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(SetLocalTransformCommand)}(index: {Index})";
}

/// <summary>
/// Records that a node should be renderable, applied via <see cref="Node.IsRenderable"/> during flush.
/// </summary>
internal sealed class SetRenderableCommand(int index, bool renderable) : ISceneCommand
{
    /// <summary>
    /// The materialized-node table slot of the target node.
    /// </summary>
    public int Index { get; } = index;

    /// <summary>
    /// The recorded renderable flag.
    /// </summary>
    public bool Renderable { get; } = renderable;

    public ResultCode Apply(World world, Node[] nodes)
    {
        nodes[Index].IsRenderable = Renderable;
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(SetRenderableCommand)}(index: {Index}, renderable: {Renderable})";
}

/// <summary>
/// Records the creation of an arbitrary <see cref="Node"/> subtype <typeparamref name="T"/>
/// through a consumer-supplied factory. The factory is stored as-is at record time and is
/// neither copied nor invoked until flush; during flush it is invoked exactly once on the
/// owning thread to materialize the concrete node, mirroring the type-erasure pattern used by
/// the ECS <c>SetComponentCommand&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="T">The concrete <see cref="Node"/> subtype the factory constructs.</typeparam>
internal sealed class CreateCustomNodeCommand<T>(int index, Func<World, T> factory) : ISceneCommand
    where T : Node
{
    /// <summary>
    /// The consumer-supplied factory. Stored by reference; never copied or invoked at record time.
    /// </summary>
    private readonly Func<World, T> _factory = factory;

    /// <summary>
    /// The slot in the materialized-node table that receives the constructed node.
    /// </summary>
    public int Index { get; } = index;

    /// <summary>
    /// Invokes the recorded factory exactly once on the owning thread to construct the concrete
    /// node and place it in the materialized-node table.
    /// </summary>
    /// <returns>
    /// <see cref="ResultCode.InvalidState"/> if the factory returns <see langword="null"/>; otherwise
    /// <see cref="ResultCode.Ok"/>.
    /// </returns>
    public ResultCode Apply(World world, Node[] nodes)
    {
        var node = _factory(world);
        if (node is null)
        {
            return ResultCode.InvalidState;
        }

        nodes[Index] = node;
        return ResultCode.Ok;
    }

    public string Describe() => $"{nameof(CreateCustomNodeCommand<T>)}<{typeof(T).Name}>(index: {Index})";
}

/// <summary>
/// Records an arbitrary consumer-supplied action to run against the <see cref="World"/> during
/// flush. The action is stored as-is at record time and is neither copied nor invoked until
/// flush; during flush it is invoked exactly once on the owning thread, in recorded order,
/// mirroring the type-erasure/deferral pattern used by <see cref="CreateCustomNodeCommand{T}"/>.
/// This keeps the Scene layer free of any rendering/instancing concepts while still allowing
/// callers to defer work onto the world's owning thread.
/// </summary>
internal sealed class DeferredActionCommand(Action<World> action, string description) : ISceneCommand
{
    /// <summary>
    /// The consumer-supplied action. Stored by reference; never copied or invoked at record time.
    /// </summary>
    private readonly Action<World> _action = action;

    /// <summary>
    /// The human-readable description surfaced in failure messages.
    /// </summary>
    private readonly string _description = description;

    /// <summary>
    /// Invokes the recorded action exactly once on the owning thread during flush, in recorded
    /// order. Any exception thrown by the action is intentionally allowed to propagate so the
    /// flush can catch it, stop, and identify this command as the point of failure.
    /// </summary>
    public ResultCode Apply(World world, Node[] nodes)
    {
        _action(world);
        return ResultCode.Ok;
    }

    public string Describe() => _description;
}
