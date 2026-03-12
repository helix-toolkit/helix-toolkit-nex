namespace HelixToolkit.Nex.ECS.Events;

internal readonly struct EntityEnableEvent(in int entityId, bool enable)
{
    public readonly int EntityId = entityId;
    public readonly bool Enabled = enable;
}

public enum ComponentOperations
{
    Added,
    Changed,
    Removed,
}

public readonly struct ComponentChangedEvent<T>(
    in int entityId,
    ComponentOperations operation,
    ComponentTypeId id
)
{
    public readonly int EntityId = entityId;
    public readonly ComponentOperations Operation = operation;
    public readonly ComponentTypeId ComponentTypeId = id;
}

internal readonly struct EntityDisposingEvent(in int entityId, in Generation generation)
{
    public readonly int EntityId = entityId;
    public readonly Generation Generation = generation;
}
