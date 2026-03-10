using HelixToolkit.Nex.DependencyInjection;

namespace HelixToolkit.Nex.Tests.DependencyInjection;

[TestClass]
public sealed class ServiceProviderTests
{
    [TestMethod]
    public void GetService_IServiceProvider_ShouldReturnSelf()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService(typeof(IServiceProvider));

        Assert.IsNotNull(result);
        Assert.AreSame(provider, result);
    }

    [TestMethod]
    public void GetService_UnregisteredService_ShouldReturnNull()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService(typeof(IDisposable));

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetRequiredService_UnregisteredService_ShouldThrow()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            provider.GetRequiredService(typeof(IDisposable));
        });

        Assert.IsTrue(exception.Message.Contains("IDisposable"));
    }

    [TestMethod]
    public void GetService_SingletonWithImplementationType_ShouldReturnSameInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetService<ITestService>();
        var instance2 = provider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void GetService_SingletonWithInstance_ShouldReturnProvidedInstance()
    {
        var services = new ServiceCollection();
        var instance = new TestService();
        services.AddSingleton<ITestService>(instance);
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<ITestService>();

        Assert.IsNotNull(result);
        Assert.AreSame(instance, result);
    }

    [TestMethod]
    public void GetService_SingletonWithFactory_ShouldReturnSameInstance()
    {
        var services = new ServiceCollection();
        var factoryCallCount = 0;
        services.AddSingleton<ITestService>(sp =>
        {
            factoryCallCount++;
            return new TestService();
        });
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetService<ITestService>();
        var instance2 = provider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreSame(instance1, instance2);
        Assert.AreEqual(1, factoryCallCount);
    }

    [TestMethod]
    public void GetService_TransientWithImplementationType_ShouldReturnDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetService<ITestService>();
        var instance2 = provider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreNotSame(instance1, instance2);
    }

    [TestMethod]
    public void GetService_TransientWithFactory_ShouldCallFactoryEachTime()
    {
        var services = new ServiceCollection();
        var factoryCallCount = 0;
        services.AddTransient<ITestService>(sp =>
        {
            factoryCallCount++;
            return new TestService();
        });
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetService<ITestService>();
        var instance2 = provider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreNotSame(instance1, instance2);
        Assert.AreEqual(2, factoryCallCount);
    }

    [TestMethod]
    public void GetService_ScopedFromRoot_ShouldReturnSameInstance()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetService<ITestService>();
        var instance2 = provider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void GetService_ScopedFromScope_ShouldReturnSameInstanceWithinScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var instance1 = scope.ServiceProvider.GetService<ITestService>();
        var instance2 = scope.ServiceProvider.GetService<ITestService>();

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void GetService_ScopedFromDifferentScopes_ShouldReturnDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        ITestService? instance1;
        ITestService? instance2;

        using (var scope1 = provider.CreateScope())
        {
            instance1 = scope1.ServiceProvider.GetService<ITestService>();
        }

        using (var scope2 = provider.CreateScope())
        {
            instance2 = scope2.ServiceProvider.GetService<ITestService>();
        }

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreNotSame(instance1, instance2);
    }

    [TestMethod]
    public void GetService_WithDependencies_ShouldResolveDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IServiceWithDependency, ServiceWithDependency>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<IServiceWithDependency>();

        Assert.IsNotNull(result);
        var service = result as ServiceWithDependency;
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.TestService);
    }

    [TestMethod]
    public void GetService_WithMultipleDependencies_ShouldResolveAll()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IAnotherService, AnotherService>();
        services.AddSingleton<IServiceWithMultipleDependencies, ServiceWithMultipleDependencies>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<IServiceWithMultipleDependencies>();

        Assert.IsNotNull(result);
        var service = result as ServiceWithMultipleDependencies;
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.TestService);
        Assert.IsNotNull(service.AnotherService);
    }

    [TestMethod]
    public void GetService_WithUnresolvableDependency_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ServiceWithValueTypeDependency>();
        var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            provider.GetService<ServiceWithValueTypeDependency>();
        });

        Assert.IsTrue(exception.Message.Contains("Int32"));
    }

    [TestMethod]
    public void GetService_WithOptionalDependency_ShouldUseDefaultValue()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceWithOptionalDependency, ServiceWithOptionalDependency>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<IServiceWithOptionalDependency>();

        Assert.IsNotNull(result);
        var service = result as ServiceWithOptionalDependency;
        Assert.IsNotNull(service);
        Assert.IsNull(service.TestService);
    }

    [TestMethod]
    public void GetService_WithNullableDependency_ShouldResolveWithNull()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceWithNullableDependency, ServiceWithNullableDependency>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<IServiceWithNullableDependency>();

        Assert.IsNotNull(result);
        var service = result as ServiceWithNullableDependency;
        Assert.IsNotNull(service);
        Assert.IsNull(service.TestService);
    }

    [TestMethod]
    public void GetService_IServiceScopeFactory_ShouldReturnProvider()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetService<IServiceScopeFactory>();

        Assert.IsNotNull(factory);
        Assert.AreSame(provider, factory);
    }

    [TestMethod]
    public void CreateScope_ShouldReturnNewScope()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.IsNotNull(scope);
        Assert.IsNotNull(scope.ServiceProvider);
        Assert.AreNotSame(provider, scope.ServiceProvider);
    }

    [TestMethod]
    public void CreateScope_SingletonsShouldBeShared()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var rootInstance = provider.GetService<ITestService>();
        ITestService? scopedInstance;

        using (var scope = provider.CreateScope())
        {
            scopedInstance = scope.ServiceProvider.GetService<ITestService>();
        }

        Assert.IsNotNull(rootInstance);
        Assert.IsNotNull(scopedInstance);
        Assert.AreSame(rootInstance, scopedInstance);
    }

    [TestMethod]
    public void Dispose_ScopedServices_ShouldDisposeServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDisposableService, DisposableService>();
        var provider = services.BuildServiceProvider();

        DisposableService? service;
        using (var scope = provider.CreateScope())
        {
            service = scope.ServiceProvider.GetService<IDisposableService>() as DisposableService;
            Assert.IsNotNull(service);
            Assert.IsFalse(service.IsDisposed);
        }

        Assert.IsTrue(service.IsDisposed);
    }

    [TestMethod]
    public void Dispose_SingletonServices_ShouldDisposeOnRootDispose()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDisposableService, DisposableService>();
        var provider = services.BuildServiceProvider();

        var service = provider.GetService<IDisposableService>() as DisposableService;
        Assert.IsNotNull(service);
        Assert.IsFalse(service.IsDisposed);

        provider.Dispose();

        Assert.IsTrue(service.IsDisposed);
    }

    [TestMethod]
    public void Dispose_TransientServices_ShouldNotBeDisposed()
    {
        // Transient services are not tracked for disposal in this simple implementation
        var services = new ServiceCollection();
        services.AddTransient<IDisposableService, DisposableService>();
        var provider = services.BuildServiceProvider();

        var service = provider.GetService<IDisposableService>() as DisposableService;
        Assert.IsNotNull(service);
        Assert.IsFalse(service.IsDisposed);

        provider.Dispose();

        // Transient services are not disposed by the container
        Assert.IsFalse(service.IsDisposed);
    }

    [TestMethod]
    public void GetService_ConstructorWithMostParameters_ShouldBeSelected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IAnotherService, AnotherService>();
        services.AddSingleton<ServiceWithMultipleConstructors>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<ServiceWithMultipleConstructors>();

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.TestService);
        Assert.IsNotNull(result.AnotherService);
        Assert.AreEqual(2, result.ConstructorParameterCount);
    }

    [TestMethod]
    public void GetService_NoPublicConstructor_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ServiceWithPrivateConstructor>();
        var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            provider.GetService<ServiceWithPrivateConstructor>();
        });

        Assert.IsTrue(exception.Message.Contains("No public constructor"));
    }

    [TestMethod]
    public void GetRequiredService_Generic_RegisteredService_ShouldReturn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetRequiredService<ITestService>();

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void GetRequiredService_Generic_UnregisteredService_ShouldThrow()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            provider.GetRequiredService<ITestService>();
        });

        Assert.IsTrue(exception.Message.Contains("ITestService"));
    }

    [TestMethod]
    public void GetService_Generic_ShouldReturnCorrectType()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<ITestService>();

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<TestService>(result);
    }

    [TestMethod]
    public void GetService_FactoryReceivesServiceProvider_ShouldResolveOtherServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IServiceWithDependency>(sp =>
        {
            var testService = sp.GetRequiredService<ITestService>();
            return new ServiceWithDependency(testService);
        });
        var provider = services.BuildServiceProvider();

        var result = provider.GetService<IServiceWithDependency>();

        Assert.IsNotNull(result);
        var service = result as ServiceWithDependency;
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.TestService);
    }

    [TestMethod]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        provider.Dispose();
        provider.Dispose(); // Should not throw
    }

    [TestMethod]
    public void CreateScope_FromScope_ShouldShareRootSingletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();

        ITestService? instance1;
        ITestService? instance2;

        using (var scope1 = provider.CreateScope())
        {
            using (var scope2 = ((IServiceScopeFactory)scope1.ServiceProvider).CreateScope())
            {
                instance1 = scope1.ServiceProvider.GetService<ITestService>();
                instance2 = scope2.ServiceProvider.GetService<ITestService>();
            }
        }

        Assert.IsNotNull(instance1);
        Assert.IsNotNull(instance2);
        Assert.AreSame(instance1, instance2);
    }

    // Test Helper Classes
    public interface ITestService { }

    public class TestService : ITestService { }

    public interface IAnotherService { }

    public class AnotherService : IAnotherService { }

    public interface IServiceWithDependency { }

    public class ServiceWithDependency : IServiceWithDependency
    {
        public ITestService TestService { get; }

        public ServiceWithDependency(ITestService testService)
        {
            TestService = testService;
        }
    }

    public interface IServiceWithMultipleDependencies { }

    public class ServiceWithMultipleDependencies : IServiceWithMultipleDependencies
    {
        public ITestService TestService { get; }
        public IAnotherService AnotherService { get; }

        public ServiceWithMultipleDependencies(
            ITestService testService,
            IAnotherService anotherService
        )
        {
            TestService = testService;
            AnotherService = anotherService;
        }
    }

    public interface IServiceWithOptionalDependency { }

    public class ServiceWithOptionalDependency : IServiceWithOptionalDependency
    {
        public ITestService? TestService { get; }

        public ServiceWithOptionalDependency(ITestService? testService = null)
        {
            TestService = testService;
        }
    }

    public interface IServiceWithNullableDependency { }

    public class ServiceWithNullableDependency : IServiceWithNullableDependency
    {
        public ITestService? TestService { get; }

        public ServiceWithNullableDependency(ITestService? testService)
        {
            TestService = testService;
        }
    }

    public interface IDisposableService { }

    public class DisposableService : IDisposableService, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    public class ServiceWithMultipleConstructors
    {
        public ITestService? TestService { get; }
        public IAnotherService? AnotherService { get; }
        public int ConstructorParameterCount { get; }

        public ServiceWithMultipleConstructors()
        {
            ConstructorParameterCount = 0;
        }

        public ServiceWithMultipleConstructors(ITestService testService)
        {
            TestService = testService;
            ConstructorParameterCount = 1;
        }

        public ServiceWithMultipleConstructors(
            ITestService testService,
            IAnotherService anotherService
        )
        {
            TestService = testService;
            AnotherService = anotherService;
            ConstructorParameterCount = 2;
        }
    }

    public class ServiceWithPrivateConstructor
    {
        private ServiceWithPrivateConstructor() { }
    }

    public class ServiceWithValueTypeDependency
    {
        public int Value { get; }

        public ServiceWithValueTypeDependency(int value)
        {
            Value = value;
        }
    }
}
