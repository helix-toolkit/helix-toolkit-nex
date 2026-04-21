using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Non-generic marker interface for an asynchronous GPU upload operation.
/// Allows heterogeneous collections of upload handles without knowing the resource type.
/// </summary>
public interface IAsyncUploadHandle
{
    /// <summary>Gets a value indicating whether the upload has completed.</summary>
    bool IsCompleted { get; }

    /// <summary>Gets the result code. Only meaningful once <see cref="IsCompleted"/> is <see langword="true"/>.</summary>
    ResultCode Result { get; }

    /// <summary>
    /// Returns the underlying <see cref="Task"/> that completes when the upload finishes.
    /// Suitable for use with <see cref="Task.WhenAll(IEnumerable{Task})"/>.
    /// </summary>
    Task WhenCompleted { get; }
}

/// <summary>
/// Represents the result of an asynchronous GPU upload operation.
/// Can be polled for completion or awaited.
/// </summary>
public sealed class AsyncUploadHandle : IAsyncUploadHandle
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

    /// <inheritdoc/>
    public Task WhenCompleted => _tcs.Task;

    /// <summary>
    /// Gets the awaiter for this handle, enabling <c>await handle</c> syntax.
    /// The awaited result is the <see cref="ResultCode"/> of the upload.
    /// </summary>
    public TaskAwaiter<ResultCode> GetAwaiter() => _tcs.Task.GetAwaiter();

    /// <summary>
    /// Creates a pre-completed <see cref="AsyncUploadHandle"/> with the given result code.
    /// </summary>
    public static AsyncUploadHandle CreateCompleted(ResultCode result)
    {
        var handle = new AsyncUploadHandle();
        handle.Complete(result);
        return handle;
    }
}

/// <summary>
/// Represents the result of an asynchronous GPU upload operation that is associated with a
/// specific resource handle of type <typeparamref name="THandle"/>.
/// <para>
/// The generic parameter lets callers know exactly which resource type was uploaded (e.g.
/// <see cref="BufferHandle"/> or <see cref="TextureHandle"/>), and the awaited/polled result
/// carries both the <see cref="ResultCode"/> and the resource handle back to the caller.
/// </para>
/// </summary>
/// <typeparam name="THandle">
/// The GPU resource handle type associated with this upload (e.g. <see cref="BufferHandle"/>,
/// <see cref="TextureHandle"/>).
/// </typeparam>
/// <example><code>
/// // Buffer upload — caller gets the BufferHandle back alongside the result code.
/// AsyncUploadHandle&lt;BufferHandle&gt; upload = ctx.UploadAsync(buf.Handle, 0, data, count);
/// var (result, handle) = await upload;
///
/// // Texture upload — same pattern for TextureHandle.
/// AsyncUploadHandle&lt;TextureHandle&gt; upload = ctx.UploadAsync(tex.Handle, range, data, count);
/// var (result, handle) = await upload;
/// </code></example>
public sealed class AsyncUploadHandle<THandle> : IAsyncUploadHandle
{
    private readonly TaskCompletionSource<(ResultCode Result, THandle Handle)> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private volatile bool _completed;

    /// <summary>
    /// Gets a value indicating whether the upload has completed (either successfully or with an error).
    /// </summary>
    public bool IsCompleted => _completed;

    /// <summary>
    /// Gets the result code of the upload. Only meaningful when <see cref="IsCompleted"/> is <see langword="true"/>.
    /// </summary>
    public ResultCode Result { get; private set; } = ResultCode.Ok;

    /// <summary>
    /// Gets the resource handle associated with the upload.
    /// Only meaningful when <see cref="IsCompleted"/> is <see langword="true"/>.
    /// </summary>
    public THandle ResourceHandle { get; private set; } = default!;

    /// <summary>
    /// Gets a <see cref="Task{T}"/> that completes when the upload finishes, yielding both
    /// the <see cref="ResultCode"/> and the associated resource handle.
    /// </summary>
    public Task<(ResultCode Result, THandle Handle)> Task => _tcs.Task;

    /// <summary>
    /// Marks the upload as complete.
    /// </summary>
    /// <param name="result">The result code of the upload operation.</param>
    /// <param name="handle">The resource handle that was uploaded to.</param>
    public void Complete(ResultCode result, THandle handle)
    {
        Result = result;
        ResourceHandle = handle;
        _completed = true;
        _tcs.TrySetResult((result, handle));
    }

    /// <summary>
    /// Creates a pre-completed <see cref="AsyncUploadHandle{THandle}"/> with the given result and handle.
    /// </summary>
    public static AsyncUploadHandle<THandle> CreateCompleted(ResultCode result, THandle handle)
    {
        var uploadHandle = new AsyncUploadHandle<THandle>();
        uploadHandle.Complete(result, handle);
        return uploadHandle;
    }

    /// <inheritdoc/>
    public Task WhenCompleted => _tcs.Task;

    /// <summary>
    /// Gets the awaiter for this handle, enabling <c>await handle</c> syntax.
    /// The awaited result is a <c>(ResultCode Result, THandle Handle)</c> tuple.
    /// </summary>
    public TaskAwaiter<(ResultCode Result, THandle Handle)> GetAwaiter() => _tcs.Task.GetAwaiter();
}
