namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents the result of an asynchronous GPU upload operation.
/// Can be polled for completion or awaited.
/// </summary>
public sealed class AsyncUploadHandle
{
    private readonly TaskCompletionSource<ResultCode> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private volatile bool _completed;

    /// <summary>
    /// Gets a value indicating whether the upload has completed (either successfully or with an error).
    /// </summary>
    public bool IsCompleted => _completed;

    /// <summary>
    /// Gets the result of the upload. Only valid when <see cref="IsCompleted"/> is true.
    /// </summary>
    public ResultCode Result { get; private set; } = ResultCode.Ok;

    /// <summary>
    /// Gets a <see cref="Task{ResultCode}"/> that completes when the upload finishes.
    /// </summary>
    public Task<ResultCode> Task => _tcs.Task;

    /// <summary>
    /// A pre-completed handle indicating success with no actual upload.
    /// </summary>
    public static readonly AsyncUploadHandle CompletedOk = CreateCompleted(ResultCode.Ok);

    /// <summary>
    /// Marks the upload as complete with the given result code.
    /// </summary>
    /// <param name="result">The result code of the upload operation.</param>
    public void Complete(ResultCode result)
    {
        Result = result;
        _completed = true;
        _tcs.TrySetResult(result);
    }

    public static AsyncUploadHandle CreateCompleted(ResultCode result)
    {
        var handle = new AsyncUploadHandle();
        handle.Complete(result);
        return handle;
    }
}
