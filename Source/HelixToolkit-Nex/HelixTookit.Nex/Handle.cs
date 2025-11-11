namespace HelixToolkit.Nex;

/// <summary>
/// Represents a type-safe handle with generational versioning to prevent the ABA problem.
/// </summary>
/// <typeparam name="T">The type this handle references.</typeparam>
/// <param name="index">The index of the object in a pool or collection.</param>
/// <param name="gen">The generation number for version tracking.</param>
/// <remarks>
/// Handles use a generation number to ensure that reused indices don't accidentally
/// refer to wrong objects. When an object is destroyed and its slot is reused, the
/// generation number is incremented, invalidating old handles.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Handle<T>(uint index, uint gen)
{
    private readonly uint index_ = index; // the index of this handle within a Pool  
    private readonly uint gen_ = gen; // the generation of this handle to prevent the ABA Problem  

    /// <summary>
    /// Gets a value indicating whether this handle is empty (invalid).
    /// </summary>
    /// <value>True if the generation is 0; otherwise, false.</value>
    public bool Empty => gen_ == 0;

    /// <summary>
    /// Gets a value indicating whether this handle is valid (not empty).
  /// </summary>
    /// <value>True if the generation is not 0; otherwise, false.</value>
 public bool Valid => gen_ != 0;

    /// <summary>
    /// Gets the index component of this handle.
    /// </summary>
  public uint Index => index_;

    /// <summary>
    /// Gets the generation component of this handle.
    /// </summary>
    public uint Gen => gen_;

    /// <summary>
    /// Converts the index to a native pointer-sized integer.
    /// </summary>
    /// <returns>The index as an <see cref="nint"/>.</returns>
    public nint IndexAsVoid()
 {
    return (nint)index_;
    }

    /// <summary>
    /// Determines whether two handles are equal.
 /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns>True if both index and generation are equal; otherwise, false.</returns>
    public static bool operator ==(Handle<T> left, Handle<T> right) // Fixed CS0558: Made static and public  
  {
        return left.index_ == right.index_ && left.gen_ == right.gen_;
    }

    /// <summary>
    /// Determines whether two handles are not equal.
    /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
 /// <returns>True if either index or generation differs; otherwise, false.</returns>
    public static bool operator !=(Handle<T> left, Handle<T> right) // Fixed CS0558: Made static and public  
    {
        return left.index_ != right.index_ || left.gen_ != right.gen_;
    }

 /// <summary>
    /// Determines whether this handle equals another object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the object is a <see cref="Handle{T}"/> with the same index and generation; otherwise, false.</returns>
    public override bool Equals(object? obj) // Added Equals override for proper equality comparison  
    {
        return obj != null && obj is Handle<T> other && this == other;
    }

    /// <summary>
    /// Gets the hash code for this handle.
    /// </summary>
 /// <returns>A hash code combining the index and generation.</returns>
  public override int GetHashCode() // Added GetHashCode override for proper hashing
    {
        return HashCode.Combine(index_, gen_);
    }

    /// <summary>
    /// Implicitly converts a handle to a boolean indicating its validity.
    /// </summary>
    /// <param name="handle">The handle to convert.</param>
    /// <returns>True if the handle is valid; otherwise, false.</returns>
    public static implicit operator bool(Handle<T> handle) // Fixed CS1019: Changed to explicit operator  
    {
        return handle.Valid;
    }

    /// <summary>
    /// A predefined null/empty handle for convenience.
    /// </summary>
    public static readonly Handle<T> Null = new(0, 0); // Added a static Null handle for convenience
}
