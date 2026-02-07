namespace HelixToolkit.Nex.DependencyInjection;

public static class ServiceProviderServiceExtensions
{
    public static T? GetService<T>(this IServiceProvider provider)
    {
        return (T?)provider.GetService(typeof(T));
    }

    public static T GetRequiredService<T>(this IServiceProvider provider)
        where T : notnull
    {
        var service = provider.GetService(typeof(T));
        if (service == null)
        {
            throw new InvalidOperationException(
                $"No service for type '{typeof(T)}' has been registered."
            );
        }
        return (T)service;
    }

    public static object GetRequiredService(this IServiceProvider provider, Type serviceType)
    {
        var service = provider.GetService(serviceType);
        if (service == null)
        {
            throw new InvalidOperationException(
                $"No service for type '{serviceType}' has been registered."
            );
        }
        return service;
    }
}
