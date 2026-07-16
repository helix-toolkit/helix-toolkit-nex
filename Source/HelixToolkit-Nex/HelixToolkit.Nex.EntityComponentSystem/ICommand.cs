namespace HelixToolkit.Nex.ECS;

/// <summary>
/// A non-generic representation of a single deferred operation recorded into a
/// command buffer. Concrete implementations capture the operation's payload during
/// recording and know how to apply themselves onto a <see cref="World"/> during flush.
/// </summary>
/// <remarks>
/// Type erasure through this non-generic interface keeps the buffer's ordered command
/// list homogeneous while the concrete generic command classes preserve full component
/// type information.
/// </remarks>
internal interface ICommand
{
    /// <summary>
    /// Applies this command onto the target <paramref name="world"/> using the
    /// resolved-entity table populated during flush.
    /// </summary>
    /// <param name="world">The target world the command is applied to.</param>
    /// <param name="resolved">
    /// The resolved-entity table. Entity-creation commands populate their slot;
    /// component commands read the entity from their slot.
    /// </param>
    /// <returns><see cref="ResultCode.Ok"/> on success; otherwise a failing code.</returns>
    ResultCode Apply(World world, Entity[] resolved);

    /// <summary>
    /// Returns a human-readable description of this command, used to identify a failed
    /// command in a <see cref="FlushResult"/>.
    /// </summary>
    /// <returns>A description of the command.</returns>
    string Describe();
}
