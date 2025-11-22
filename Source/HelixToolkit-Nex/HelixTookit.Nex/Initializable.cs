namespace HelixToolkit.Nex;

/// <summary>
/// Base class for objects requiring initialization and cleanup.
/// </summary>
public abstract class Initializable : IDisposable
{
    private bool disposedValue;

    /// <summary>
    /// Gets whether the object has been initialized.
    /// </summary>
    public bool IsInitialized { private set; get; } = false;

    /// <summary>
    /// Name of this object.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Initializes the object if not already initialized.
    /// </summary>
    /// <returns>True if initialization succeeded or already initialized.</returns>
    public bool Setup()
    {
        if (IsInitialized)
        {
            return true;
        }
        IsInitialized = OnInitializing();
        return IsInitialized;
    }

    /// <summary>
    /// Override to implement initialization logic.
    /// </summary>
    /// <returns>True if initialization succeeded.</returns>
    protected abstract bool OnInitializing();

    /// <summary>
    /// Tears down the object if initialized.
    /// </summary>
    public void TearDown()
    {
        if (!IsInitialized)
        {
            return;
        }
        OnTearDown();
        IsInitialized = false;
    }

    /// <summary>
    /// Override to implement cleanup logic.
    /// </summary>
    protected abstract void OnTearDown();

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                TearDown();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
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
