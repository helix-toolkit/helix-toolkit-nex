using System.Diagnostics.CodeAnalysis;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// A recorded remove-component command for component type <typeparamref name="T"/>.
/// During flush it removes the <typeparamref name="T"/> component from the resolved
/// <see cref="Entity"/>.
/// </summary>
/// <typeparam name="T">The component type to remove.</typeparam>
internal sealed class RemoveComponentCommand<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T> : ICommand
{
    /// <summary>
    /// The slot in the resolved-entity table identifying the target entity.
    /// </summary>
    public int Index { get; }

    internal RemoveComponentCommand(int index)
    {
        Index = index;
    }

    /// <inheritdoc/>
    public ResultCode Apply(World world, Entity[] resolved)
    {
        // Removing a component that was never set is a harmless no-op under direct
        // application (Entity.Remove<T> returns NotFound without mutating the world).
        // To preserve record-then-flush equivalence with direct application
        // (Requirement 5.1), treat NotFound as success so it does not abort the flush.
        // Any other non-Ok code is a genuine failure and is surfaced to the caller so
        // that Flush can abort and report the offending command (Requirement 4.6).
        var code = resolved[Index].Remove<T>();
        return code == ResultCode.NotFound ? ResultCode.Ok : code;
    }

    /// <inheritdoc/>
    public string Describe() => $"{nameof(RemoveComponentCommand<T>)}<{typeof(T).Name}>(Index={Index})";
}
