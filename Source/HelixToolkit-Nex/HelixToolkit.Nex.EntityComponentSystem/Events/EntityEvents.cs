namespace HelixToolkit.Nex.ECS.Events;

internal readonly struct EntityEnableEvent(in int entityId, bool enable)
{
    public readonly int EntityId = entityId;
    public readonly bool Enabled = enable;
}

internal enum ComponentOperations
{
    Added,
    Changed,
    Removed,
}

internal readonly struct ComponentChangedEvent<T>(in int entityId, ComponentOperations operation)
{
    public readonly int EntityId = entityId;
    public readonly ComponentOperations Operation = operation;
}

internal readonly struct EntityDisposingEvent(in int entityId, in Generation generation)
{
    public readonly int EntityId = entityId;
    public readonly Generation Generation = generation;
}
