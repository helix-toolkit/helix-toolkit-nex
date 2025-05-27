namespace HelixToolkit.Nex;

public static class Disposer
{
    public static void DisposeAndRemove<T>(ref T? disposable) where T : IDisposable
    {
        if (disposable != null)
        {
            disposable.Dispose();
            disposable = default;
        }
    }
}
