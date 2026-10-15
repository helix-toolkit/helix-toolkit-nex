namespace HelixToolkit.Nex.DependencyInjection;

public interface IServiceCollection : IList<ServiceDescriptor> { }

public class ServiceCollection : List<ServiceDescriptor>, IServiceCollection { }
