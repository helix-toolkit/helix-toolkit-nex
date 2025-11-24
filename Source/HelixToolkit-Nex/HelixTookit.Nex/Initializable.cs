namespace HelixToolkit.Nex;

/// <summary>
/// Represents an object that requires explicit initialization and cleanup lifecycle management.
/// </summary>
/// <remarks>
/// This interface extends <see cref="IDisposable"/> to provide a structured initialization and teardown pattern.
/// Objects implementing this interface can be explicitly initialized and torn down, with state tracking.
/// </remarks>
public interface IInitializable : IDisposable
{
    /// <summary>
    /// Gets the name of the object for identification and logging purposes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the object has been successfully initialized.
    /// </summary>
    /// <value><c>true</c> if the object is initialized and not disposed; otherwise, <c>false</c>.</value>
    bool IsInitialized { get; }

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    /// <value><c>true</c> if the object has been disposed; otherwise, <c>false</c>.</value>
    bool IsDisposed { get; }

    /// <summary>
    /// Initializes the object if not already initialized.
    /// </summary>
    /// <returns>Result code</returns>
    ResultCode Initialize();

    /// <summary>
    /// Tears down the object if initialized.
    /// </summary>
    /// <returns>Result code</returns>
    ResultCode TearDown();
}

/// <summary>
/// Base class for objects requiring initialization and cleanup lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// This abstract class provides a complete implementation of the <see cref="IInitializable"/> interface,
/// including initialization state tracking, logging, and proper disposal pattern.
/// </para>
/// <para>
/// Derived classes must implement <see cref="OnInitializing"/> and <see cref="OnTearingDown"/>
/// to define their specific initialization and cleanup logic.
/// </para>
/// </remarks>
public abstract class Initializable : IInitializable
{
    private static readonly ILogger _logger = LogManager.Create<Initializable>();
    private bool _disposedValue;

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    /// <value><c>true</c> if the object has been disposed; otherwise, <c>false</c>.</value>
    public bool IsDisposed => _disposedValue;

    private bool _isInitialized;

    /// <summary>
    /// Gets a value indicating whether the object has been successfully initialized and not disposed.
    /// </summary>
    /// <value><c>true</c> if the object is initialized and not disposed; otherwise, <c>false</c>.</value>
    public bool IsInitialized => _isInitialized && !IsDisposed;

    /// <summary>
    /// Gets the name of this object for identification and logging purposes.
    /// </summary>
    /// <value>A string that uniquely identifies this object instance.</value>
    public abstract string Name { get; }

    /// <summary>
    /// Initializes the object if not already initialized.
    /// </summary>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> if initialization succeeds or the object is already initialized;
    /// otherwise, an error code indicating the reason for failure.
    /// </returns>
    /// <remarks>
    /// This method is idempotent - calling it multiple times on an already initialized object
    /// will return <see cref="ResultCode.Ok"/> without re-initializing.
    /// The initialization process is logged for debugging purposes.
    /// </remarks>
    public ResultCode Initialize()
    {
        if (IsInitialized)
        {
            return ResultCode.Ok;
        }
        _logger.LogTrace("[{Name}]: Initializing", Name);
        var ret = OnInitializing();
        _isInitialized = ret == ResultCode.Ok;
        if (IsInitialized)
        {
            _logger.LogTrace("[{Name}]: Initialized", Name);
        }
        else
        {
            _logger.LogError("[{Name}]: Not initialized. Code: {Result}", Name, ret);
        }
        return ret;
    }

    /// <summary>
    /// When overridden in a derived class, performs the actual initialization logic.
    /// </summary>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> if initialization succeeds;
    /// otherwise, an error code indicating the reason for failure.
    /// </returns>
    /// <remarks>
    /// Derived classes should implement their specific initialization logic in this method.
    /// This method is called by <see cref="Initialize"/> when the object is not yet initialized.
    /// </remarks>
    protected abstract ResultCode OnInitializing();

    /// <summary>
    /// Tears down the object if initialized, releasing associated resources.
    /// </summary>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> if teardown succeeds or the object is not initialized;
    /// otherwise, an error code indicating the reason for failure.
    /// </returns>
    /// <remarks>
    /// This method is idempotent - calling it on an uninitialized object will return
    /// <see cref="ResultCode.Ok"/> without performing any teardown.
    /// The teardown process is logged for debugging purposes.
    /// After teardown, the object is marked as not initialized.
    /// </remarks>
    public ResultCode TearDown()
    {
        if (!IsInitialized)
        {
            return ResultCode.Ok;
        }
        _logger.LogTrace("[{Name}]: Tearing down", Name);
        var ret = OnTearingDown();
        if (ret != ResultCode.Ok)
        {
            _logger.LogError("[{Name}]: Tearing down failed. Code: {Result}", Name, ret);
        }
        else
        {
            _logger.LogTrace("[{Name}]: Torn down", Name);
        }
        _isInitialized = false;
        return ret;
    }

    /// <summary>
    /// When overridden in a derived class, performs the actual cleanup logic.
    /// </summary>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> if teardown succeeds;
    /// otherwise, an error code indicating the reason for failure.
    /// </returns>
    /// <remarks>
    /// Derived classes should implement their specific cleanup logic in this method.
    /// This method is called by <see cref="TearDown"/> when the object is initialized.
    /// Resources allocated during <see cref="OnInitializing"/> should be released here.
    /// </remarks>
    protected abstract ResultCode OnTearingDown();

    /// <summary>
    /// Releases the resources used by the <see cref="Initializable"/> object.
    /// </summary>
    /// <param name="disposing">
    /// <c>true</c> to release both managed and unmanaged resources;
    /// <c>false</c> to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// <para>
    /// When <paramref name="disposing"/> is <c>true</c>, this method releases all resources held by managed objects
    /// by calling <see cref="TearDown"/> to ensure proper cleanup.
    /// </para>
    /// <para>
    /// Derived classes can override this method to dispose of additional resources.
    /// When overriding, always call the base class implementation.
    /// </para>
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _logger.LogTrace("[{Name}]: Disposing", Name);
                TearDown();
                _logger.LogTrace("[{Name}]: Disposed", Name);
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Initializable()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
