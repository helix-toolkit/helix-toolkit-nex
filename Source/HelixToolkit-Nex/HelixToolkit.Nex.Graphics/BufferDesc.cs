namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Bitflags that describe how a buffer will be used within the graphics system.
/// These values can be combined using a bitwise OR to indicate multiple usages.
/// </summary>
/// <remarks>
/// The underlying storage of the enum is <c>uint8_t</c>. Treat this enum as a set
/// of independent usage flags rather than mutually-exclusive values.
/// </remarks>
[Flags]
public enum BufferUsageBits : uint8_t
{
    /// <summary>
    /// No usage flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Buffer will be used as an index buffer for indexed draws.
    /// </summary>
    Index = 1 << 0,

    /// <summary>
    /// Buffer will be used as a vertex buffer.
    /// </summary>
    Vertex = 1 << 1,

    /// <summary>
    /// Buffer will be bound as a uniform (constant) buffer.
    /// </summary>
    Uniform = 1 << 2,

    /// <summary>
    /// Buffer will be used as a general-purpose storage buffer (shader storage).
    /// </summary>
    Storage = 1 << 3,

    /// <summary>
    /// Buffer will be used for indirect draw/dispatch arguments.
    /// </summary>
    Indirect = 1 << 4,

    // ray tracing

    /// <summary>
    /// Buffer contains a shader binding table used for ray tracing.
    /// </summary>
    ShaderBindingTable = 1 << 5,

    /// <summary>
    /// Buffer contains build-input data for acceleration structure construction and is read-only.
    /// </summary>
    AccelStructBuildInputReadOnly = 1 << 6,

    /// <summary>
    /// Buffer is used as storage for acceleration structures.
    /// </summary>
    AccelStructStorage = 1 << 7
}

/// <summary>
/// Describes the properties required to create or initialize a GPU/graphics buffer.
/// </summary>
/// <param name="usage">Bitflags describing the buffer's permitted usages (<see cref="BufferUsageBits"/>).</param>
/// <param name="storage">The memory/storage type where the buffer will reside (<see cref="StorageType"/>).</param>
/// <param name="data">
/// Pointer to the initial data to upload into the buffer. The pointer type is <c>nint</c>.
/// If <c>IntPtr.Zero</c> (or equivalent) is provided, no initial data is uploaded.
/// </param>
/// <param name="dataSize">Size in bytes of the data pointed to by <paramref name="data"/> (<c>size_t</c>).</param>
/// <param name="debugName">Optional friendly name used for debugging and profiling. Defaults to an empty string.</param>
/// <remarks>
/// - The lifetime/ownership of the memory pointed to by <paramref name="data"/> is the caller's responsibility.
///   The buffer creation code may copy the data synchronously during creation; ensure the memory remains valid
///   until the call that consumes it returns.
/// - <see cref="DataSize"/> should match the actual size of the data in bytes. Passing incorrect sizes may result
///   in truncated or out-of-bounds uploads.
/// - Use <see cref="BufferUsageBits"/> to combine multiple usages (for example: <c>BufferUsageBits.Vertex | BufferUsageBits.Storage</c>).
/// </remarks>
public struct BufferDesc(BufferUsageBits usage, StorageType storage, nint data, size_t dataSize, string? debugName = null)
{
    /// <summary>
    /// Bitflags indicating how the buffer will be used.
    /// </summary>
    public BufferUsageBits Usage = usage;

    /// <summary>
    /// The memory/storage type for the buffer (device-local, host-visible, memoryless, etc.).
    /// </summary>
    public StorageType Storage = storage;

    /// <summary>
    /// Pointer to the initial data to upload into the buffer. May be <c>IntPtr.Zero</c> if no data is provided.
    /// </summary>
    public nint Data = data;

    /// <summary>
    /// Size in bytes of the memory pointed to by <see cref="Data"/>.
    /// </summary>
    public size_t DataSize = dataSize;

    /// <summary>
    /// Optional debug/profiling name for the buffer. Never null; defaults to an empty string.
    /// </summary>
    public string DebugName = debugName ?? string.Empty;
}
