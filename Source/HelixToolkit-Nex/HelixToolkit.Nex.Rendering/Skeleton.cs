namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Shared skinning asset: the inverse bind matrices of a joint hierarchy (a glTF <c>skin</c>).
/// </summary>
/// <remarks>
/// A skeleton is immutable after construction and may be shared by any number of
/// <see cref="Skinning"/> instances. Its GPU buffer is created lazily by <see cref="UpdateBuffer"/>.
/// The owner is responsible for disposing it once no <see cref="Skinning"/> references it.
/// </remarks>
public sealed class Skeleton : IDisposable
{
    private readonly FastList<Matrix4x4> _inverseBindMatrices;
    private ElementBuffer<Matrix4x4>? _inverseBindBuffer;
    private bool _disposedValue;

    /// <summary>
    /// Gets the skeleton name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the inverse bind matrices, one per joint. Each matrix transforms a vertex from the
    /// skinned mesh's bind space into the joint's local space.
    /// </summary>
    public IReadOnlyList<Matrix4x4> InverseBindMatrices => _inverseBindMatrices;

    /// <summary>
    /// Gets the number of joints.
    /// </summary>
    public int JointCount => _inverseBindMatrices.Count;

    /// <summary>
    /// Gets the GPU buffer holding the inverse bind matrices. Null until <see cref="UpdateBuffer"/> is called.
    /// </summary>
    public BufferResource InverseBindBuffer => _inverseBindBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Initializes a new instance of the <see cref="Skeleton"/> class.
    /// </summary>
    /// <param name="inverseBindMatrices">The inverse bind matrices, one per joint. Must not be empty.</param>
    /// <param name="name">Optional skeleton name.</param>
    public Skeleton(IEnumerable<Matrix4x4> inverseBindMatrices, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(inverseBindMatrices);
        _inverseBindMatrices = [.. inverseBindMatrices];
        if (_inverseBindMatrices.Count == 0)
        {
            throw new ArgumentException(
                "A skeleton must have at least one joint.",
                nameof(inverseBindMatrices)
            );
        }
        Name = name ?? $"Skeleton_{GetHashCode():X}";
    }

    /// <summary>
    /// Creates the inverse bind matrix GPU buffer if it does not exist yet.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode UpdateBuffer(IContext context)
    {
        ObjectDisposedException.ThrowIf(_disposedValue, this);
        if (_inverseBindBuffer is not null)
        {
            return ResultCode.Ok;
        }
        _inverseBindBuffer = new ElementBuffer<Matrix4x4>(
            context,
            _inverseBindMatrices,
            BufferUsageBits.Storage,
            hostVisible: false,
            debugName: $"{Name}_InvBind"
        );
        return _inverseBindBuffer.Buffer.Valid ? ResultCode.Ok : ResultCode.OutOfMemory;
    }

    public void Dispose()
    {
        if (_disposedValue)
        {
            return;
        }
        Disposer.DisposeAndRemove(ref _inverseBindBuffer);
        _disposedValue = true;
    }
}
