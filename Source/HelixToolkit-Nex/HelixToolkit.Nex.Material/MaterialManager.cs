using System.Collections.Concurrent;
using HelixToolkit.Nex.DependencyInjection;

namespace HelixToolkit.Nex.Material;

public sealed class MaterialManager(IServiceProvider services) : IMaterialManager
{
    private readonly ConcurrentDictionary<MaterialTypeId, Material> _materials = new();

    public IContext Context { get; } = services.GetRequiredService<IContext>();

    public int Count => _materials.Count;

    public MaterialPropertyCreator CreateMaterial(string name, RenderPipelineDesc pipelineDesc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNullOrEmpty(name);

        var material = new Material(name);

        if (!material.CreatePipeline(Context, pipelineDesc))
        {
            throw new InvalidOperationException("Failed to create material pipeline");
        }
        if (!_materials.TryAdd(material.MaterialId, material))
        {
            material.Dispose();
            throw new InvalidOperationException("Failed to add material to repository");
        }
        return new MaterialPropertyCreator(
            material.MaterialId,
            services.GetRequiredService<IMaterialPropertyManager>()
        );
    }

    public void DestroyMaterial(MaterialTypeId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_materials.TryGetValue(id, out var material))
        {
            material.Dispose();
            _materials.TryRemove(id, out _);
        }
    }

    public RenderPipelineHandle GetMaterialPipeline(MaterialTypeId materialType)
    {
        return _materials.TryGetValue(materialType, out var material)
            ? material.Pipeline
            : RenderPipelineHandle.Null;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var material in _materials.Values)
        {
            material.Dispose();
        }
        _materials.Clear();
    }

    #region IDisposable Support

    private bool _disposed;

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Clear();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MaterialManager()
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
    #endregion
}
