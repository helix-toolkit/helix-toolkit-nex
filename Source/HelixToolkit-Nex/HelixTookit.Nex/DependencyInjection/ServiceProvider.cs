using System.Collections.Concurrent;
using System.Reflection;

namespace HelixToolkit.Nex.DependencyInjection;

public class ServiceProvider : IServiceProvider, IDisposable, IServiceScopeFactory
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors;
    private readonly ConcurrentDictionary<Type, object> _singletons = new();
    private readonly ConcurrentDictionary<Type, object> _scopedInstances = new();
    private readonly ServiceProvider? _root;
    private readonly bool _isScope;

    internal ServiceProvider(IServiceCollection services)
    {
        _descriptors = services.ToDictionary(d => d.ServiceType, d => d);
        _root = null;
        _isScope = false;

        // Register IServiceScopeFactory
        _singletons[typeof(IServiceScopeFactory)] = this;
    }

    internal ServiceProvider(ServiceProvider root)
    {
        _descriptors = root._descriptors;
        _root = root;
        _isScope = true;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
        {
            return null;
        }

        if (descriptor.Lifetime == ServiceLifetime.Singleton)
        {
            if (_root != null)
            {
                return _root.GetService(serviceType);
            }
            return _singletons.GetOrAdd(serviceType, t => CreateInstance(descriptor));
        }

        if (descriptor.Lifetime == ServiceLifetime.Scoped)
        {
            if (!_isScope)
            {
                // If we are root (not a scope), we treat scoped as singleton or we could throw.
                // Microsoft DI treats scoped services resolved from root as singletons effectively,
                // but typically validates scopes. For simplicity here, if resolved from root, we'll store in root's scoped dictionary so they act like singletons/root-scoped.
                return _scopedInstances.GetOrAdd(serviceType, t => CreateInstance(descriptor));
            }
            return _scopedInstances.GetOrAdd(serviceType, t => CreateInstance(descriptor));
        }

        // Transient
        return CreateInstance(descriptor);
    }

    public T? GetService<T>()
    {
        return (T?)GetService(typeof(T));
    }

    public object GetRequiredService(Type serviceType)
    {
        var service = GetService(serviceType);
        if (service == null)
        {
            throw new InvalidOperationException(
                $"No service for type '{serviceType}' has been registered."
            );
        }
        return service;
    }

    public T GetRequiredService<T>()
        where T : notnull
    {
        return (T)GetRequiredService(typeof(T));
    }

    private object CreateInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory != null)
        {
            return descriptor.ImplementationFactory(this);
        }

        if (descriptor.ImplementationType != null)
        {
            var constructors = descriptor.ImplementationType.GetConstructors();
            // Simple selection: pick constructor with most parameters that can be satisfied
            // For now, let's just pick the public constructor. If multiple, assume the one with most parameters.
            var constructor = constructors
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor == null)
            {
                throw new InvalidOperationException(
                    $"No public constructor found for {descriptor.ImplementationType}"
                );
            }

            var parameters = constructor.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var service = GetService(paramType);
                if (service == null)
                {
                    if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue!;
                    }
                    else if (IsNullable(parameters[i]))
                    {
                        args[i] = null!;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Unable to resolve service for type '{paramType}' while attempting to activate '{descriptor.ImplementationType}'."
                        );
                    }
                }
                else
                {
                    args[i] = service;
                }
            }

            return constructor.Invoke(args);
        }

        throw new InvalidOperationException(
            $"Invalid service descriptor for {descriptor.ServiceType}"
        );
    }

    private static bool IsNullable(ParameterInfo parameter)
    {
        // Check for Nullable<T> or reference types if in nullable context (but runtime check is hard for nullable ref types without attributes)
        // For simplicity, handle Nullable<T> and classes.
        // In .NET 8, nullable attributes are available but complex to parse manually without help.
        // Let's assume if it's not a value type, it 'can' be null, or if it is Nullable<T>.
        return !parameter.ParameterType.IsValueType
            || Nullable.GetUnderlyingType(parameter.ParameterType) != null;
    }

    public IServiceScope CreateScope()
    {
        // The scope should rely on the root for singletons, but have its own storage for scoped services.
        // If 'this' is already a scope, a new scope should still probably point to the root for singletons?
        // Actually, normally scope factory is a singleton service.
        // Let's implement IServiceScopeFactory pattern or just method here.
        var root = _root ?? this;
        return new ServiceScope(new ServiceProvider(root));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var instance in _scopedInstances.Values)
            {
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else if (instance is IAsyncDisposable asyncDisposable)
                {
                    // Sync dispose of async disposable if possible, otherwise... fire and forget?
                    // Bad practice in sync method. For this minimal DI, we focus on IDisposable.
                    // If necessary, we can check for IAsyncDisposable and run it blocking (risky) or ignore.
                }
            }
            _scopedInstances.Clear();

            if (_root == null) // Only root disposes singletons
            {
                foreach (var instance in _singletons.Values)
                {
                    if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _singletons.Clear();
            }
        }
    }
}
