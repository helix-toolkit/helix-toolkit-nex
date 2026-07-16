namespace HelixToolkit.Nex.ECS;

/// <summary>
/// A recorded entity-creation command. During flush it creates a real
/// <see cref="Entity"/> in the target world and stores it into the resolved-entity
/// table at <see cref="Index"/>.
/// </summary>
internal sealed class CreateEntityCommand : ICommand
{
    /// <summary>
    /// The slot in the resolved-entity table that the created entity is stored into.
    /// </summary>
    public int Index { get; }

    internal CreateEntityCommand(int index)
    {
        Index = index;
    }

    /// <inheritdoc/>
    public ResultCode Apply(World world, Entity[] resolved)
    {
        resolved[Index] = world.CreateEntity();
        return ResultCode.Ok;
    }

    /// <inheritdoc/>
    public string Describe() => $"{nameof(CreateEntityCommand)}(Index={Index})";
}
