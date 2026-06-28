using System.Diagnostics.CodeAnalysis;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Records deferred entity-creation, set-component, and remove-component operations into
/// its own storage without touching any <see cref="World"/>, and applies them in recorded
/// order during flush onto a world's owning thread.
/// </summary>
/// <remarks>
/// Recording is single-writer: a given buffer is expected to be recorded from a single
/// recording thread. No <see cref="World"/> is referenced during recording, which makes the
/// "no World access during recording" contract structurally true. A buffer recorded on one
/// thread may be flushed on another; the buffer itself carries no thread affinity.
/// </remarks>
public sealed class CommandBuffer
{
    /// <summary>
    /// Source of unique, non-zero buffer identities. Each <see cref="CommandBuffer"/> takes
    /// the next value so that handles produced by different buffers never collide.
    /// </summary>
    private static int _idCounter;

    /// <summary>
    /// Unique, non-zero identity for this buffer, stamped into every <see cref="DeferredEntity"/>
    /// it produces. Used to reject handles produced by a different buffer.
    /// </summary>
    private readonly int _id;

    /// <summary>
    /// Ordered list of recorded commands, preserving the order in which they were recorded.
    /// </summary>
    private readonly List<ICommand> _commands = [];

    /// <summary>
    /// Number of entity-creation commands recorded so far. Also the next deferred-entity
    /// index and the size of the resolved-entity table allocated at flush.
    /// </summary>
    private int _deferredCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandBuffer"/> class with a unique,
    /// non-zero identity.
    /// </summary>
    public CommandBuffer()
    {
        // Interlocked.Increment never yields the initial 0, so _id is always non-zero,
        // matching DeferredEntity.IsValid's "OwnerId != 0" contract.
        _id = Interlocked.Increment(ref _idCounter);
    }

    /// <summary>
    /// Gets the number of commands recorded into this buffer that have not yet been flushed.
    /// </summary>
    public int PendingCount => _commands.Count;

    /// <summary>
    /// Records an entity-creation command and returns a handle that resolves to a real
    /// <see cref="Entity"/> during flush.
    /// </summary>
    /// <returns>
    /// A distinct <see cref="DeferredEntity"/> for each call, owned by this buffer.
    /// </returns>
    public DeferredEntity RecordCreateEntity()
    {
        var index = _deferredCount++;
        _commands.Add(new CreateEntityCommand(index));
        return new DeferredEntity(index, _id);
    }

    /// <summary>
    /// Records a set-component command for the given deferred entity, storing a copy of the
    /// supplied component value for application during flush.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="entity">The deferred entity to set the component on.</param>
    /// <param name="component">The component value; a copy is captured at record time.</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> when the command is recorded;
    /// <see cref="ResultCode.InvalidState"/> when <paramref name="entity"/> was not produced by
    /// this buffer, in which case no command is appended.
    /// </returns>
    public ResultCode RecordSet<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(DeferredEntity entity, T component)
    {
        if (!OwnsHandle(entity))
        {
            return ResultCode.InvalidState;
        }
        _commands.Add(new SetComponentCommand<T>(entity.Index, component));
        return ResultCode.Ok;
    }

    /// <summary>
    /// Records a remove-component command for the given deferred entity.
    /// </summary>
    /// <typeparam name="T">The component type to remove.</typeparam>
    /// <param name="entity">The deferred entity to remove the component from.</param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> when the command is recorded;
    /// <see cref="ResultCode.InvalidState"/> when <paramref name="entity"/> was not produced by
    /// this buffer, in which case no command is appended.
    /// </returns>
    public ResultCode RecordRemove<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(DeferredEntity entity)
    {
        if (!OwnsHandle(entity))
        {
            return ResultCode.InvalidState;
        }
        _commands.Add(new RemoveComponentCommand<T>(entity.Index));
        return ResultCode.Ok;
    }

    /// <summary>
    /// Applies all recorded commands to <paramref name="world"/> in recorded order, on the
    /// world's owning thread, resolving every <see cref="DeferredEntity"/> to a real
    /// <see cref="Entity"/>.
    /// </summary>
    /// <param name="world">The target world the commands are applied to.</param>
    /// <returns>
    /// <see cref="FlushResult.Ok"/> when every command applied successfully and the buffer
    /// is left empty; otherwise a failing <see cref="FlushResult"/> that identifies the
    /// offending command by position and description.
    /// </returns>
    /// <remarks>
    /// A <c>null</c> world or a disposed world (whose <see cref="World.Id"/> has been reset
    /// to <c>0</c>) is rejected with <see cref="ResultCode.WorldNotValid"/> and nothing is
    /// mutated. If a command fails mid-flush, the commands already applied remain in the
    /// world, all remaining pending commands are cleared, and the failing result identifies
    /// the command that failed.
    /// </remarks>
    public FlushResult Flush(World world)
    {
        // Disposed-world detection: Dispose resets Id to 0 (see World.Dispose).
        if (world is null || world.Id == 0)
        {
            return FlushResult.Failed(ResultCode.WorldNotValid, -1, "Target world is null or disposed.");
        }

        var resolved = new Entity[_deferredCount];
        for (var i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            var code = command.Apply(world, resolved);
            if (code != ResultCode.Ok)
            {
                // Stop, clear all remaining pending commands (including the failed one),
                // and report the failing command's position and description.
                var message = command.Describe();
                _commands.Clear();
                _deferredCount = 0;
                return FlushResult.Failed(code, i, message);
            }
        }

        // Success: the buffer becomes empty so a subsequent flush is a no-op.
        _commands.Clear();
        _deferredCount = 0;
        return FlushResult.Ok;
    }

    /// <summary>
    /// Test-only hook that appends an arbitrary <see cref="ICommand"/> to the ordered
    /// command list, allowing tests to deterministically construct a command that fails
    /// during <see cref="Flush"/>. The command is appended at the current end of the list,
    /// so its position equals <see cref="PendingCount"/> at the time of the call. This does
    /// not affect the deferred-entity count and does not alter the public recording API.
    /// </summary>
    /// <param name="command">The command to append.</param>
    internal void AppendCommandForTest(ICommand command) => _commands.Add(command);

    /// <summary>
    /// Determines whether the given handle is valid and was produced by this buffer.
    /// </summary>
    /// <param name="entity">The handle to validate.</param>
    /// <returns>
    ///   <c>true</c> when the handle is valid, owned by this buffer, and references a
    ///   recorded creation slot; otherwise, <c>false</c>.
    /// </returns>
    private bool OwnsHandle(DeferredEntity entity)
        => entity.IsValid
            && entity.OwnerId == _id
            && entity.Index >= 0
            && entity.Index < _deferredCount;
}
