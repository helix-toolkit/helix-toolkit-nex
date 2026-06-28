namespace HelixToolkit.Nex;

/// <summary>
/// Result codes for graphics operations.
/// </summary>
public enum ResultCode
{
    /// <summary>
    /// Unknown or unspecified result.
    /// </summary>
    Unknown,

    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Ok,

    /// <summary>
    /// An argument was outside the valid range.
    /// </summary>
    ArgumentOutOfRange,

    /// <summary>
    /// A runtime error occurred during execution.
    /// </summary>
    RuntimeError,

    /// <summary>
    /// The requested operation or feature is not supported.
    /// </summary>
    NotSupported,

    /// <summary>
    /// An argument had an invalid value.
    /// </summary>
    ArgumentError,

    /// <summary>
    /// The system ran out of memory.
    /// </summary>
    OutOfMemory,

    /// <summary>
    /// A required argument was null.
    /// </summary>
    ArgumentNull,

    /// <summary>
    /// The operation is invalid in the current state.
    /// </summary>
    InvalidState,

    /// <summary>
    /// A compilation error occurred (e.g., shader compilation).
    /// </summary>
    CompileError,

    /// <summary>
    /// Not ready state.
    /// </summary>
    NotReady,

    /// <summary>
    /// The requested item (e.g., an entity component) was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The target world is null, disposed, or otherwise not in a valid state.
    /// </summary>
    WorldNotValid,

    /// <summary>
    /// The entity does not belong to the world the operation was issued against.
    /// </summary>
    NotThisWorld,

    /// <summary>
    /// The entities involved in the operation do not belong to the same world.
    /// </summary>
    NotTheSameWorld,
}
