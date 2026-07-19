namespace HelixToolkit.Nex.DependencyInjection;

public interface IServiceScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }
}

public class ServiceScope : IServiceScope
{
    private readonly ServiceProvider _serviceProvider;

    public ServiceScope(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServiceProvider ServiceProvider => _serviceProvider;

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
