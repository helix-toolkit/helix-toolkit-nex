using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Material;

public class MaterialManager(IContext context, IMaterialPropertyManager propertyManager)
    : IMaterialManager
{
    private readonly ConcurrentDictionary<MaterialTypeId, Material> _materials = new();

    public IContext Context { get; } = context;

    public IMaterialPropertyManager MaterialPropertyManager { get; } = propertyManager;

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
        return new MaterialPropertyCreator(material.MaterialId, MaterialPropertyManager);
    }

    public virtual int CreatePBRMaterialsFromRegistry()
    {
        var builder = new MaterialShaderBuilder();
        using var result = builder
            .WithForwardPlus(true)
            .WithUberShader()
            .BuildMaterialPipeline(Context, "UberShader");

        foreach (var material in MaterialTypeRegistry.GetAllRegistrations())
        {
            var materialPipelineDesc = new RenderPipelineDesc
            {
                VertexShader = result.VertexShader,
                FragmentShader = result.FragmentShader,
                Topology = Topology.Triangle,
                CullMode = CullMode.Back,
                DepthFormat = Format.Z_F32,
                PolygonMode = PolygonMode.Fill,
                DebugName = material.Name,
            };
            materialPipelineDesc.Colors[0] = ColorAttachment.CreateAlphaBlend(Format.RGBA_F16);
            CreateMaterial(material.Name, materialPipelineDesc);
        }
        return MaterialTypeRegistry.GetAllRegistrations().Count;
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
