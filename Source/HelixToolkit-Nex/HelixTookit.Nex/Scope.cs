namespace HelixToolkit.Nex;

/// <summary>
/// Represents a disposable scope that executes an action when disposed.
/// </summary>
/// <param name="actionOnDispose">The action to execute when the scope is disposed.</param>
/// <remarks>
/// This class is useful for implementing custom resource management patterns,
/// such as temporarily changing state and restoring it upon disposal.
/// </remarks>
public sealed class Scope(Action actionOnDispose) : IDisposable
{
    private readonly Action actionOnDispose = actionOnDispose;

    private bool disposedValue;

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                actionOnDispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Scope()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    /// <summary>
    /// Disposes the scope and executes the action provided in the constructor.
    /// </summary>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
}
