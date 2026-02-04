namespace HelixToolkit.Nex;

/// <summary>
/// A simple logger factory that creates <see cref="DebugLogger"/> instances.
/// </summary>
/// <remarks>
/// This factory is used internally to create loggers that write to the debug output window.
/// </remarks>
internal sealed class DebugLoggerFactory : ILoggerFactory
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new DebugLogger(categoryName);
    }

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
