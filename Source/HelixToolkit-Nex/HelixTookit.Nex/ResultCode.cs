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
}
