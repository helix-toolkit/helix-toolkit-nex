namespace HelixToolkit.Nex;

/// <summary>
/// Provides static utility methods for managing <see cref="IDisposable"/> objects.
/// </summary>
public static class Disposer
{
    /// <summary>
    /// Disposes an <see cref="IDisposable"/> object and sets the reference to null.
    /// </summary>
    /// <typeparam name="T">The type of the disposable object.</typeparam>
    /// <param name="disposable">Reference to the disposable object. Will be set to null after disposal.</param>
    /// <remarks>
    /// This method is safe to call even if <paramref name="disposable"/> is already null.
    /// After disposal, the reference is set to the default value (null for reference types).
    /// </remarks>
    public static void DisposeAndRemove<T>(ref T? disposable)
        where T : IDisposable
    {
        if (disposable != null)
        {
            disposable.Dispose();
            disposable = default;
        }
    }
}
