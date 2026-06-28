namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A lightweight handle returned when a node-creation command is recorded into a
/// <see cref="SceneCommandBuffer"/>. A <see cref="DeferredNode"/> resolves to a real
/// <see cref="Node"/> only during flush.
/// </summary>
/// <remarks>
/// The handle carries an <see cref="Index"/> into the producing buffer's materialized-node
/// table and an <see cref="OwnerId"/> identifying the producing scene command buffer.
/// The owner id lets a buffer reject handles that were produced by a different buffer
/// without any shared global state.
/// </remarks>
public readonly struct DeferredNode : IEquatable<DeferredNode>
{
    /// <summary>
    /// The slot in the producing buffer's materialized-node table that this handle maps to.
    /// </summary>
    internal int Index { get; }

    /// <summary>
    /// The identity of the producing <see cref="SceneCommandBuffer"/>. A value of zero denotes
    /// an invalid handle.
    /// </summary>
    internal int OwnerId { get; }

    /// <summary>
    /// Gets a value indicating whether this handle was produced by a <see cref="SceneCommandBuffer"/>.
    /// </summary>
    /// <value>
    ///   <c>true</c> if the handle has a non-zero owner; otherwise, <c>false</c>.
    /// </value>
    public bool IsValid => OwnerId != 0;

    internal DeferredNode(int index, int ownerId)
    {
        Index = index;
        OwnerId = ownerId;
    }

    #region Operator

    public static bool operator ==(DeferredNode a, DeferredNode b) => a.Equals(b);

    public static bool operator !=(DeferredNode a, DeferredNode b) => !a.Equals(b);

    #endregion

    #region Object

    public bool Equals(DeferredNode other) => other.Index == Index && other.OwnerId == OwnerId;

    public override bool Equals(object? obj) => obj is DeferredNode other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, OwnerId);

    public override string ToString() => $"{nameof(DeferredNode)} {OwnerId}:{Index}";

    #endregion
}
