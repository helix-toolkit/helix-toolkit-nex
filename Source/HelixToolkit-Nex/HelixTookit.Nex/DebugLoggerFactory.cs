namespace HelixToolkit.Nex;

internal sealed class DebugLoggerFactory : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        return new DebugLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
