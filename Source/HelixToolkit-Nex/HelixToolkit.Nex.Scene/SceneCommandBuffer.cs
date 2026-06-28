// ResultCode refers to the single graphics enum HelixToolkit.Nex.ResultCode, resolved from the
// enclosing HelixToolkit.Nex namespace; the scene layer uses its InvalidState/NotReady/Ok members.
namespace HelixToolkit.Nex.Scene;
/// <summary>
/// A scene-layer object that records deferred <see cref="Node"/> creation, parent/child
/// relationships, and node properties (name, local transform, renderable flag) on a recording
/// thread without touching any <see cref="World"/>, and materializes them as real
/// <see cref="Node"/> objects during <see cref="Flush"/> on the world's owning thread.
/// </summary>
/// <remarks>
/// <para>
/// Recording never accepts or references a <see cref="World"/>, so the "no World access during
/// recording" contract is structurally true. Each recorded node-creation returns a
/// <see cref="DeferredNode"/> handle that resolves to a real <see cref="Node"/> only during flush.
/// </para>
/// <para>
/// Recording is single-writer: one recording thread records into a given buffer. The buffer
/// carries no thread affinity itself, so a buffer recorded on one thread can be flushed on the
/// owning thread of the target world.
/// </para>
/// </remarks>
public sealed class SceneCommandBuffer
{
    private static int _nextId;

    private readonly int _id;
    private readonly List<ISceneCommand> _sceneCommands = [];

    /// <summary>
    /// Number of node-creation commands recorded; sizes the materialized-node table at flush.
    /// </summary>
    private int _nodeCount;

    /// <summary>
    /// Parent index recorded per node (<c>-1</c> means no recorded parent). Grows by one entry
    /// for each recorded node so an already-recorded parent can be detected at record time.
    /// </summary>
    private readonly List<int> _recordedParent = [];

    /// <summary>
    /// Materialized nodes, populated during a successful flush.
    /// </summary>
    private readonly Dictionary<DeferredNode, Node> _materialized = [];

    /// <summary>
    /// Single-writer re-entrancy flag (<c>0</c> = idle, <c>1</c> = a recording operation is in
    /// progress). Each public recording method brackets its body with an
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> on this field so a concurrent
    /// second writer is rejected while the buffer's recorded state is preserved, enforcing the
    /// single-writer contract without paying for locks on the common single-threaded path.
    /// </summary>
    private int _recordingGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneCommandBuffer"/> class with a unique,
    /// non-zero identity stamped into every <see cref="DeferredNode"/> it produces.
    /// </summary>
    public SceneCommandBuffer()
    {
        // Interlocked.Increment yields 1 on first call, so the id is always non-zero and a
        // default DeferredNode (OwnerId == 0) is never mistaken for one of this buffer's handles.
        _id = Interlocked.Increment(ref _nextId);
    }

    /// <summary>
    /// Gets the number of recorded commands not yet flushed.
    /// </summary>
    public int PendingCount => _sceneCommands.Count;

    /// <summary>
    /// Gets the materialized nodes keyed by their <see cref="DeferredNode"/> handle. Valid only
    /// after a successful <see cref="Flush"/>.
    /// </summary>
    public IReadOnlyDictionary<DeferredNode, Node> MaterializedNodes => _materialized;

    /// <summary>
    /// Records a node-creation command and returns a handle that resolves to a real
    /// <see cref="Node"/> during flush.
    /// </summary>
    /// <returns>
    /// A distinct, valid <see cref="DeferredNode"/> handle; a default (invalid) handle if a
    /// concurrent recording operation is in progress (single-writer contract).
    /// </returns>
    public DeferredNode RecordCreateNode()
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            // Another recording operation is in progress: reject, leave state unchanged.
            return default;
        }

        try
        {
            return RecordCreateNodeCore();
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Records a node-creation command together with a name, and returns a handle that resolves
    /// to a real <see cref="Node"/> during flush.
    /// </summary>
    /// <param name="name">The name to apply to the materialized node.</param>
    /// <returns>
    /// A distinct, valid <see cref="DeferredNode"/> handle; a default (invalid) handle if a
    /// concurrent recording operation is in progress (single-writer contract).
    /// </returns>
    public DeferredNode RecordCreateNode(string name)
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            return default;
        }

        try
        {
            var node = RecordCreateNodeCore();
            _sceneCommands.Add(new SetNameCommand(node.Index, name));
            return node;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Records creation of an arbitrary <see cref="Node"/> subtype <typeparamref name="T"/>
    /// through a consumer-supplied <paramref name="factory"/> delegate, returning a typed handle
    /// that resolves to a real <typeparamref name="T"/> during flush. The factory is stored, not
    /// invoked, and runs exactly once on the owning thread during <see cref="Flush"/>.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="Node"/> subtype the factory constructs.</typeparam>
    /// <param name="factory">
    /// A delegate that constructs a <typeparamref name="T"/> from a <see cref="World"/> when
    /// invoked during flush. All type-specific data must be captured inside the delegate. Neither
    /// the delegate nor any <see cref="World"/> method is invoked during recording.
    /// </param>
    /// <param name="handle">
    /// On success, a distinct, valid <see cref="TypedDeferredNode{T}"/>; otherwise a default
    /// (invalid) handle.
    /// </param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> on success; <see cref="ResultCode.InvalidState"/> if
    /// <paramref name="factory"/> is <see langword="null"/> or a concurrent recording operation is
    /// in progress. On rejection no command is appended and previously recorded commands are left
    /// unchanged.
    /// </returns>
    public ResultCode TryRecordCreateNode<T>(Func<World, T> factory, out TypedDeferredNode<T> handle)
        where T : Node
    {
        if (factory is null)
        {
            // Null factory: reject, append no command, leave prior commands unchanged.
            handle = default;
            return ResultCode.InvalidState;
        }

        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            // Another recording operation is in progress: reject, leave state unchanged.
            handle = default;
            return ResultCode.InvalidState;
        }

        try
        {
            // Consume one slot in the shared node table so base and custom nodes interleave in
            // one buffer. The factory is stored by reference and is neither copied nor invoked.
            var index = _nodeCount++;
            _recordedParent.Add(-1);
            _sceneCommands.Add(new CreateCustomNodeCommand<T>(index, factory));
            handle = new TypedDeferredNode<T>(new DeferredNode(index, _id));
            return ResultCode.Ok;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Convenience overload of <see cref="TryRecordCreateNode{T}"/> that returns the typed handle
    /// directly.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="Node"/> subtype the factory constructs.</typeparam>
    /// <param name="factory">The node factory; see <see cref="TryRecordCreateNode{T}"/>.</param>
    /// <returns>
    /// A valid <see cref="TypedDeferredNode{T}"/> on success; a handle whose
    /// <see cref="TypedDeferredNode{T}.IsValid"/> is <see langword="false"/> if
    /// <paramref name="factory"/> is <see langword="null"/> or a concurrent recording operation is
    /// in progress (no command appended).
    /// </returns>
    public TypedDeferredNode<T> RecordCreateNode<T>(Func<World, T> factory)
        where T : Node
    {
        TryRecordCreateNode(factory, out var handle);
        return handle;
    }

    /// <summary>
    /// Retrieves the materialized node for a <see cref="TypedDeferredNode{T}"/>, typed as
    /// <typeparamref name="T"/>, after a successful <see cref="Flush"/>.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="Node"/> subtype the handle was recorded with.</typeparam>
    /// <param name="handle">The typed handle produced by this buffer.</param>
    /// <param name="node">
    /// On <see cref="ResultCode.Ok"/>, the materialized node typed as <typeparamref name="T"/>;
    /// otherwise <see langword="null"/>. Repeated retrievals for the same handle return the
    /// reference-equal same instance.
    /// </param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> when the node has been materialized;
    /// <see cref="ResultCode.NotReady"/> (with <paramref name="node"/> <see langword="null"/>) when
    /// the handle has not yet been materialized by a successful flush;
    /// <see cref="ResultCode.InvalidState"/> when the handle was not produced by this buffer.
    /// </returns>
    public ResultCode TryGetMaterializedNode<T>(TypedDeferredNode<T> handle, out T? node)
        where T : Node
    {
        node = null;

        // Foreign-handle check by owner id. The buffer's id is non-zero, so a default handle
        // (OwnerId == 0) is also rejected here. _nodeCount is reset after a successful flush, so
        // ownership is validated by id rather than by the (now-cleared) recorded slot range.
        var deferred = handle.Handle;
        if (deferred.OwnerId != _id)
        {
            return ResultCode.InvalidState;
        }

        // Not materialized yet (no successful flush has resolved this handle).
        if (!_materialized.TryGetValue(deferred, out var materialized))
        {
            return ResultCode.NotReady;
        }

        // The factory's return type is T, so the stored runtime type is T or a subtype and the
        // cast always succeeds. The dictionary holds a single instance, so repeated retrievals
        // return the reference-equal same node.
        node = (T)materialized;
        return ResultCode.Ok;
    }

    /// <summary>
    /// Appends a base node-creation command and reserves its materialized-table slot. Callers must
    /// hold the recording guard.
    /// </summary>
    private DeferredNode RecordCreateNodeCore()
    {
        var index = _nodeCount++;
        _recordedParent.Add(-1);
        _sceneCommands.Add(new CreateNodeCommand(index));
        return new DeferredNode(index, _id);
    }

    /// <summary>
    /// Records a parent-child relationship between two deferred nodes for application during flush.
    /// </summary>
    /// <param name="parent">The parent node handle produced by this buffer.</param>
    /// <param name="child">The child node handle produced by this buffer.</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> on success;
    /// <see cref="ResultCode.InvalidState"/> if either handle was not produced by this buffer, if
    /// <paramref name="parent"/> and <paramref name="child"/> are the same handle (self-parent),
    /// if <paramref name="child"/> already has a recorded parent, or if a concurrent recording
    /// operation is in progress. No command is appended on rejection.
    /// </returns>
    public ResultCode RecordAddChild(DeferredNode parent, DeferredNode child)
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            return ResultCode.InvalidState;
        }

        try
        {
            if (!Owns(parent) || !Owns(child))
            {
                return ResultCode.InvalidState;
            }

            // Self-parent: the same handle supplied as both parent and child is rejected.
            if (parent.Equals(child))
            {
                return ResultCode.InvalidState;
            }

            if (_recordedParent[child.Index] != -1)
            {
                return ResultCode.InvalidState;
            }

            _recordedParent[child.Index] = parent.Index;
            _sceneCommands.Add(new AddChildCommand(parent.Index, child.Index));
            return ResultCode.Ok;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Records a name to apply to the materialized node during flush.
    /// </summary>
    /// <param name="node">The node handle produced by this buffer.</param>
    /// <param name="name">The name to apply.</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> on success; <see cref="ResultCode.InvalidState"/> if the handle was
    /// not produced by this buffer.
    /// </returns>
    public ResultCode RecordName(DeferredNode node, string name)
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            return ResultCode.InvalidState;
        }

        try
        {
            if (!Owns(node))
            {
                return ResultCode.InvalidState;
            }

            _sceneCommands.Add(new SetNameCommand(node.Index, name));
            return ResultCode.Ok;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Records a local <see cref="Transform"/> to apply to the materialized node during flush.
    /// </summary>
    /// <param name="node">The node handle produced by this buffer.</param>
    /// <param name="transform">The local transform value (copied at record time).</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> on success; <see cref="ResultCode.InvalidState"/> if the handle was
    /// not produced by this buffer.
    /// </returns>
    public ResultCode RecordLocalTransform(DeferredNode node, in Transform transform)
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            return ResultCode.InvalidState;
        }

        try
        {
            if (!Owns(node))
            {
                return ResultCode.InvalidState;
            }

            _sceneCommands.Add(new SetLocalTransformCommand(node.Index, in transform));
            return ResultCode.Ok;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Records that the materialized node should be renderable during flush.
    /// </summary>
    /// <param name="node">The node handle produced by this buffer.</param>
    /// <param name="renderable">The renderable flag to apply.</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> on success; <see cref="ResultCode.InvalidState"/> if the handle was
    /// not produced by this buffer.
    /// </returns>
    public ResultCode RecordRenderable(DeferredNode node, bool renderable = true)
    {
        if (Interlocked.CompareExchange(ref _recordingGuard, 1, 0) != 0)
        {
            return ResultCode.InvalidState;
        }

        try
        {
            if (!Owns(node))
            {
                return ResultCode.InvalidState;
            }

            _sceneCommands.Add(new SetRenderableCommand(node.Index, renderable));
            return ResultCode.Ok;
        }
        finally
        {
            Volatile.Write(ref _recordingGuard, 0);
        }
    }

    /// <summary>
    /// Materializes all recorded commands onto <paramref name="world"/> on the world's owning
    /// thread, in recorded order, constructing real <see cref="Node"/> objects and wiring their
    /// hierarchy and properties.
    /// </summary>
    /// <param name="world">The target world on whose owning thread the flush runs.</param>
    /// <returns>
    /// <see cref="SceneFlushResult.Ok"/> when every command materialized successfully and the
    /// buffer is left empty; otherwise a failing <see cref="SceneFlushResult"/> that identifies
    /// the offending command by position and description.
    /// </returns>
    /// <remarks>
    /// A <c>null</c> world or a disposed world (whose <see cref="World.Id"/> has been reset to
    /// <c>0</c>) is rejected with <see cref="ResultCode.WorldNotValid"/> and nothing is mutated.
    /// Materialization replays commands through the real <see cref="Node"/> API
    /// (<c>new Node(world)</c> and <see cref="Node.AddChild"/>), so structure and level assignment
    /// are identical to direct construction. If a command returns a non-<see cref="ResultCode.Ok"/>
    /// code or any command (including a recorded factory or <see cref="Node.AddChild"/>) throws,
    /// the flush stops, clears all remaining pending state, and returns a failing result
    /// identifying the command; for a thrown exception the message includes the exception's
    /// message. Nodes materialized from the successfully applied prefix remain in the world.
    /// </remarks>
    public SceneFlushResult Flush(World world)
    {
        // Disposed-world detection: Dispose resets Id to 0 (see World.Dispose).
        if (world is null || world.Id == 0)
        {
            return SceneFlushResult.Failed(ResultCode.WorldNotValid, -1, "Target world is null or disposed.");
        }

        var nodes = new Node[_nodeCount];
        for (var i = 0; i < _sceneCommands.Count; i++)
        {
            var command = _sceneCommands[i];
            ResultCode code;
            try
            {
                code = command.Apply(world, nodes);
            }
            catch (Exception ex)
            {
                // A mid-flush throw can originate from Node.AddChild (the child already has a
                // parent in the world) or from a recorded factory invoked by
                // CreateCustomNodeCommand<T>.Apply. Stop, clear all remaining pending state, and
                // identify the failing command, surfacing the thrown exception's message. Nodes
                // materialized from the successfully applied prefix remain in the world.
                var failMessage = $"{command.Describe()}: {ex.Message}";
                ClearPending();
                return SceneFlushResult.Failed(ResultCode.InvalidState, i, failMessage);
            }

            if (code != ResultCode.Ok)
            {
                // Stop, clear all remaining pending state (including the failed command),
                // and report the failing command's position and description.
                var message = command.Describe();
                ClearPending();
                return SceneFlushResult.Failed(code, i, message);
            }
        }

        // Success: expose the materialized nodes keyed by their deferred handle, then clear
        // pending state so a subsequent flush is a no-op.
        for (var index = 0; index < _nodeCount; index++)
        {
            _materialized[new DeferredNode(index, _id)] = nodes[index];
        }

        _sceneCommands.Clear();
        _nodeCount = 0;
        _recordedParent.Clear();
        return SceneFlushResult.Ok;
    }

    /// <summary>
    /// Clears all pending recorded state, leaving the buffer empty.
    /// </summary>
    private void ClearPending()
    {
        _sceneCommands.Clear();
        _nodeCount = 0;
        _recordedParent.Clear();
    }

    /// <summary>
    /// Determines whether the supplied handle was produced by this buffer and maps to a recorded node.
    /// </summary>
    private bool Owns(DeferredNode node)
        => node.OwnerId == _id && node.Index >= 0 && node.Index < _nodeCount;
}
