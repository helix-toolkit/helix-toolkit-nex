namespace HelixToolkit.Nex.Geometries;

public enum InstancingChangeOp
{
    Added,
    Updated,
    Removed,
}

public readonly struct InstancingUpdatedEvent(Instancing instancing, InstancingChangeOp changeType) : IEvent
{
    public readonly Instancing Instancing { get; } = instancing;
    public readonly InstancingChangeOp ChangeType { get; } = changeType;
}
