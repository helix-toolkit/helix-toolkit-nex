namespace HelixToolkit.Nex.ECS;

/// <summary>
/// A lightweight handle returned when an entity-creation command is recorded into a
/// command buffer. A <see cref="DeferredEntity"/> resolves to a real
/// <see cref="Entity"/> only during flush.
/// </summary>
/// <remarks>
/// The handle carries an <see cref="Index"/> into the producing buffer's resolved-entity
/// table and an <see cref="OwnerId"/> identifying the producing command buffer.
/// The owner id lets a buffer reject handles that were produced by a different buffer
/// without any shared global state.
/// </remarks>
public readonly struct DeferredEntity : IEquatable<DeferredEntity>
{
    /// <summary>
    /// The slot in the producing buffer's resolved-entity table that this handle maps to.
    /// </summary>
    internal int Index { get; }

    /// <summary>
    /// The identity of the producing <see cref="CommandBuffer"/>. A value of zero denotes
    /// an invalid handle.
    /// </summary>
    internal int OwnerId { get; }

    /// <summary>
    /// Gets a value indicating whether this handle was produced by a <see cref="CommandBuffer"/>.
    /// </summary>
    /// <value>
    ///   <c>true</c> if the handle has a non-zero owner; otherwise, <c>false</c>.
    /// </value>
    public bool IsValid => OwnerId != 0;

    internal DeferredEntity(int index, int ownerId)
    {
        Index = index;
        OwnerId = ownerId;
    }

    #region Operator

    public static bool operator ==(DeferredEntity a, DeferredEntity b) => a.Equals(b);

    public static bool operator !=(DeferredEntity a, DeferredEntity b) => !a.Equals(b);

    #endregion

    #region Object

    public bool Equals(DeferredEntity other) => other.Index == Index && other.OwnerId == OwnerId;

    public override bool Equals(object? obj) => obj is DeferredEntity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, OwnerId);

    public override string ToString() => $"{nameof(DeferredEntity)} {OwnerId}:{Index}";

    #endregion
}
