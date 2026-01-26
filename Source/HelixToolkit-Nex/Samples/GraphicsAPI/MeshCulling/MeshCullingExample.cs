using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Shaders;

namespace MeshCulling;

internal class MeshCullingExample(IContext context) : IDisposable
{
    private readonly IContext _context = context;

    private bool _disposedValue;

    public void Initialize(int width, int height)
    {
        var cullConst = new CullingConstants() { };
    }

    public void Render(ICommandBuffer cmdBuffer, int width, int height)
    {
        // Rendering code here
    }

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
    // ~MeshCullingExample()
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
