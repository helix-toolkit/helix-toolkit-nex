// See https://aka.ms/new-console-template for more information
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Examples;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;

internal class DepthPrepassTest(IContext context) : IDisposable
{
    private readonly IContext _context = context;
    private IServiceProvider? _serviceProvider;

    public void Initialize(int width, int height)
    {
        ServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IContext), _context));
        services.AddSingleton<IGeometryManager, GeometryManager>();
        services.AddSingleton<IShaderRepository, ShaderRepository>();
        services.AddSingleton<IMaterialPropertyPool, MaterialPropertyPool>();
        services.AddSingleton<IRenderDataProvider, WorldDataProvider>();
        services.AddSingleton<ResourceManager, ResourceManager>();
        _serviceProvider = services.BuildServiceProvider();

        var dataProvider = _serviceProvider.GetRequiredService<IRenderDataProvider>();
    }

    public void Render(ICommandBuffer command, Handle<Texture> target, int width, int height) { }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~DepthPrepassTest()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
