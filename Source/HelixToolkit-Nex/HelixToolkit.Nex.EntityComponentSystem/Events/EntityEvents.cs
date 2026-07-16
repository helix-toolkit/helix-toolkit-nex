namespace HelixToolkit.Nex.ECS.Events;

internal readonly struct EntityEnableEvent(Entity entity, bool enable)
{
    public readonly Entity Entity = entity;
    public readonly bool Enabled = enable;
}

public enum ComponentOperations
{
    Added,
    Changed,
    Removed,
}

public readonly struct ComponentChangedEvent<T>(
    Entity entity,
    ComponentOperations operation,
    ComponentTypeId id
)
{
    public readonly Entity Entity = entity;
    public readonly ComponentOperations Operation = operation;
    public readonly ComponentTypeId ComponentTypeId = id;
}

internal readonly struct EntityDisposingEvent(Entity entity)
{
    public readonly Entity Entity = entity;
}

internal readonly struct EntityBeforeDisposeEvent(Entity entity)
{
    public readonly Entity Entity = entity;
}
