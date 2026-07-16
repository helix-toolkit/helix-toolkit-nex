using System.Diagnostics.CodeAnalysis;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// A recorded set-component command for component type <typeparamref name="T"/>.
/// The component value is copied at record time and applied to the resolved
/// <see cref="Entity"/> during flush.
/// </summary>
/// <typeparam name="T">The component type.</typeparam>
internal sealed class SetComponentCommand<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T> : ICommand
{
    /// <summary>
    /// The slot in the resolved-entity table identifying the target entity.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// A copy of the component value captured at record time.
    /// </summary>
    private T _value;

    internal SetComponentCommand(int index, T value)
    {
        Index = index;
        // Value types are copied by assignment, capturing the value as of record time.
        _value = value;
    }

    /// <inheritdoc/>
    public ResultCode Apply(World world, Entity[] resolved)
    {
        return resolved[Index].Set(ref _value);
    }

    /// <inheritdoc/>
    public string Describe() => $"{nameof(SetComponentCommand<T>)}<{typeof(T).Name}>(Index={Index})";
}
