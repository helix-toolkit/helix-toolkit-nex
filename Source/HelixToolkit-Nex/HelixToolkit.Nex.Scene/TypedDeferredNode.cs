namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A <see cref="DeferredNode"/> handle that additionally carries the static node subtype
/// <typeparamref name="T"/>, so that a successfully materialized node can be retrieved as
/// <typeparamref name="T"/> without a cast by the caller.
/// </summary>
/// <typeparam name="T">The concrete <see cref="Node"/> subtype the recorded factory produces.</typeparam>
/// <remarks>
/// The typed handle wraps the same buffer-owned <see cref="DeferredNode"/> that the untyped
/// path uses; the wrapped handle is the single source of truth for identity and ownership.
/// The implicit conversion to <see cref="DeferredNode"/> lets a typed handle flow into every
/// existing operation that accepts a <see cref="DeferredNode"/> (parent/child, name,
/// local-transform, renderable) with no new overloads, producing results identical to
/// supplying the equivalent <see cref="DeferredNode"/>.
/// </remarks>
public readonly struct TypedDeferredNode<T> : IEquatable<TypedDeferredNode<T>>
    where T : Node
{
    /// <summary>
    /// The wrapped buffer-owned handle that identifies this node within its producing buffer.
    /// </summary>
    internal DeferredNode Handle { get; }

    /// <summary>
    /// Gets a value indicating whether the wrapped handle was produced by a
    /// <see cref="SceneCommandBuffer"/>.
    /// </summary>
    /// <value>
    ///   <c>true</c> if the wrapped handle has a non-zero owner; otherwise, <c>false</c>.
    /// </value>
    public bool IsValid => Handle.IsValid;

    internal TypedDeferredNode(DeferredNode handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Implicitly converts a <see cref="TypedDeferredNode{T}"/> to the wrapped
    /// <see cref="DeferredNode"/> so typed handles can be supplied to every existing
    /// <see cref="DeferredNode"/>-accepting operation with no new overloads.
    /// </summary>
    /// <param name="typed">The typed handle to convert.</param>
    public static implicit operator DeferredNode(TypedDeferredNode<T> typed) => typed.Handle;

    #region Operator

    public static bool operator ==(TypedDeferredNode<T> a, TypedDeferredNode<T> b) => a.Equals(b);

    public static bool operator !=(TypedDeferredNode<T> a, TypedDeferredNode<T> b) => !a.Equals(b);

    #endregion

    #region Object

    public bool Equals(TypedDeferredNode<T> other) => Handle.Equals(other.Handle);

    public override bool Equals(object? obj) => obj is TypedDeferredNode<T> other && Equals(other);

    public override int GetHashCode() => Handle.GetHashCode();

    public override string ToString() => $"{nameof(TypedDeferredNode<T>)}<{typeof(T).Name}> {Handle}";

    #endregion
}
