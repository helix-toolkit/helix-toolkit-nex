namespace HelixToolkit.Nex.ECS;

/// <summary>
/// The result of applying all recorded commands of a command buffer onto a
/// target <see cref="World"/> via flush.
/// </summary>
public readonly struct FlushResult
{
    /// <summary>
    /// Gets the result code. <see cref="ResultCode.Ok"/> on success.
    /// </summary>
    public ResultCode Code { get; }

    /// <summary>
    /// Gets the index of the command that failed to apply, or <c>-1</c> when
    /// <see cref="Code"/> is <see cref="ResultCode.Ok"/>.
    /// </summary>
    public int FailedCommandIndex { get; }

    /// <summary>
    /// Gets a human-readable description of the failed command, or <c>null</c> on success.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets a value indicating whether the flush completed successfully.
    /// </summary>
    /// <value>
    ///   <c>true</c> if <see cref="Code"/> is <see cref="ResultCode.Ok"/>; otherwise, <c>false</c>.
    /// </value>
    public bool Success => Code == ResultCode.Ok;

    internal FlushResult(ResultCode code, int failedCommandIndex, string? message)
    {
        Code = code;
        FailedCommandIndex = failedCommandIndex;
        Message = message;
    }

    /// <summary>
    /// Gets a successful flush result.
    /// </summary>
    public static FlushResult Ok => new(ResultCode.Ok, -1, null);

    /// <summary>
    /// Creates a failing flush result that identifies the offending command.
    /// </summary>
    /// <param name="code">The non-<see cref="ResultCode.Ok"/> result code.</param>
    /// <param name="failedCommandIndex">The position of the failed command, or <c>-1</c> when not applicable.</param>
    /// <param name="message">A description of the failure.</param>
    /// <returns>A failing <see cref="FlushResult"/>.</returns>
    internal static FlushResult Failed(ResultCode code, int failedCommandIndex, string? message)
        => new(code, failedCommandIndex, message);

    public override string ToString()
        => Success
            ? $"{nameof(FlushResult)} {{ {nameof(Success)} = true }}"
            : $"{nameof(FlushResult)} {{ {nameof(Code)} = {Code}, {nameof(FailedCommandIndex)} = {FailedCommandIndex}, {nameof(Message)} = {Message} }}";
}
