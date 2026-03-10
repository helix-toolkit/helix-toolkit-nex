namespace HelixToolkit.Nex.DependencyInjection;

public class ServiceDescriptor
{
    public Type ServiceType { get; }
    public Type? ImplementationType { get; }
    public object? ImplementationInstance { get; }
    public Func<IServiceProvider, object>? ImplementationFactory { get; }
    public ServiceLifetime Lifetime { get; }

    public ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }

    public ServiceDescriptor(Type serviceType, object instance)
    {
        ServiceType = serviceType;
        ImplementationInstance = instance;
        Lifetime = ServiceLifetime.Singleton;
    }

    public ServiceDescriptor(
        Type serviceType,
        Func<IServiceProvider, object> factory,
        ServiceLifetime lifetime
    )
    {
        ServiceType = serviceType;
        ImplementationFactory = factory;
        Lifetime = lifetime;
    }
}
