using System.Diagnostics.CodeAnalysis;

namespace HelixToolkit.Nex.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                typeof(TImplementation),
                ServiceLifetime.Singleton
            )
        );
        return services;
    }

    public static IServiceCollection AddSingleton<TService>(
        this IServiceCollection services,
        TService implementationInstance
    )
        where TService : class
    {
        services.Add(new ServiceDescriptor(typeof(TService), implementationInstance));
        return services;
    }

    public static IServiceCollection AddSingleton<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                (sp) => implementationFactory(sp),
                ServiceLifetime.Singleton
            )
        );
        return services;
    }

    public static IServiceCollection AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Singleton)
        );
        return services;
    }

    public static IServiceCollection AddScoped<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(
            new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped)
        );
        return services;
    }

    public static IServiceCollection AddScoped<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                (sp) => implementationFactory(sp),
                ServiceLifetime.Scoped
            )
        );
        return services;
    }

    public static IServiceCollection AddScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Scoped)
        );
        return services;
    }

    public static IServiceCollection AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                typeof(TImplementation),
                ServiceLifetime.Transient
            )
        );
        return services;
    }

    public static IServiceCollection AddTransient<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                (sp) => implementationFactory(sp),
                ServiceLifetime.Transient
            )
        );
        return services;
    }

    public static IServiceCollection AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class
    {
        services.Add(
            new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Transient)
        );
        return services;
    }

    public static ServiceProvider BuildServiceProvider(this IServiceCollection services)
    {
        return new ServiceProvider(services);
    }
}
