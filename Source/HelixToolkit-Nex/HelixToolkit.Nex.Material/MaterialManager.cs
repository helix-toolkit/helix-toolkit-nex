using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Material;

public class MaterialManager(IContext context, IMaterialPropertyManager propertyManager)
    : IMaterialManager
{
    private readonly ConcurrentDictionary<MaterialTypeId, PBRMaterial> _materials = new();
    private readonly ConcurrentDictionary<string, MaterialTypeId> _nameToId = new();
    private readonly MaterialShaderResult?[] _uberShaderResults = new MaterialShaderResult?[
        (int)MaterialPassType.Count
    ];

    public IContext Context { get; } = context;

    public IMaterialPropertyManager MaterialPropertyManager { get; } = propertyManager;

    public int Count => _materials.Count;

    private IList<RenderPipelineDesc> CreateDefaultUberPipelineDesc(string? debugName)
    {
        var descSets = new RenderPipelineDesc[(int)MaterialPassType.Count];

        {
            var idx = (int)MaterialPassType.Opaque;
            _uberShaderResults[idx] ??= new MaterialShaderBuilder()
                .WithForwardPlus(true)
                .WithUberShader()
                .BuildMaterialPipeline(Context, "UberShader");
            var desc = new RenderPipelineDesc
            {
                VertexShader = _uberShaderResults[idx]!.VertexShader,
                FragmentShader = _uberShaderResults[idx]!.FragmentShader,
                Topology = Topology.Triangle,
                CullMode = CullMode.Back,
                DepthFormat = RenderSettings.DepthBufferFormat,
                PolygonMode = PolygonMode.Fill,
                DebugName = debugName ?? string.Empty,
            };
            desc.Colors[0] = ColorAttachment.CreateAlphaBlend(
                RenderSettings.IntermediateTargetFormat
            );

            descSets[(int)MaterialPassType.Opaque] = desc;
        }

        {
            var idx = (int)MaterialPassType.Transparent;
            _uberShaderResults[idx] ??= new MaterialShaderBuilder()
                .WithForwardPlus(true)
                .WithUberShader()
                .WithDefine("TRANSPARENT_PASS")
                .BuildMaterialPipeline(Context, "UberShader");
            var desc = new RenderPipelineDesc
            {
                VertexShader = _uberShaderResults[idx]!.VertexShader,
                FragmentShader = _uberShaderResults[idx]!.FragmentShader,
                Topology = Topology.Triangle,
                CullMode = CullMode.Back,
                DepthFormat = RenderSettings.DepthBufferFormat,
                PolygonMode = PolygonMode.Fill,
                DebugName = debugName ?? string.Empty,
            };
            // WBOIT accumulation (RGBA16F): additive blend (ONE / ONE).
            // Each fragment writes vec4(color.rgb * alpha * w, alpha * w) and all
            // contributions are summed together.
            desc.Colors[0] = ColorAttachment.CreateWboitAccumulation(Format.RGBA_F16);
            // WBOIT revealage (R16F): multiplicative blend (ZERO / ONE_MINUS_SRC_COLOR).
            // Buffer is cleared to 1.0; each fragment outputs alpha, and the hardware
            // computes dst = dst * (1 - alpha), yielding the product of all (1 - alpha_i).
            desc.Colors[1] = ColorAttachment.CreateWboitRevealage(Format.R_F16);
            descSets[(int)MaterialPassType.Transparent] = desc;
        }
        return descSets;
    }

    public MaterialPropertyCreator CreateMaterial(
        string name,
        Func<string, PBRMaterial> builderFunc
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNullOrEmpty(name);
        var material = builderFunc(name);

        if (!material.Initialize(Context, CreateDefaultUberPipelineDesc(name)))
        {
            throw new InvalidOperationException("Failed to create material pipeline");
        }
        if (!_materials.TryAdd(material.MaterialId, material))
        {
            material.Dispose();
            throw new InvalidOperationException("Failed to add material to repository");
        }
        _nameToId.TryAdd(name, material.MaterialId);
        return new MaterialPropertyCreator(material.MaterialId, MaterialPropertyManager);
    }

    public int CreatePBRMaterialsFromRegistry()
    {
        foreach (var material in MaterialTypeRegistry.GetAllRegistrations())
        {
            CreateMaterial(material.Name, material.BuilderFunction);
        }
        return MaterialTypeRegistry.GetAllRegistrations().Count;
    }

    public void DestroyMaterial(MaterialTypeId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_materials.TryRemove(id, out var material))
        {
            _nameToId.TryRemove(material.Name, out _);
            material.Dispose();
        }
    }

    public bool TryGetMaterialByName(string name, out PBRMaterial? material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nameToId.TryGetValue(name, out var id))
        {
            return _materials.TryGetValue(id, out material);
        }
        material = null;
        return false;
    }

    public RenderPipelineHandle GetMaterialPipeline(
        MaterialTypeId materialType,
        MaterialPassType type
    )
    {
        return _materials.TryGetValue(materialType, out var material)
            ? material.Pipelines[(int)type]
            : RenderPipelineHandle.Null;
    }

    public PBRMaterial? GetMaterial(MaterialTypeId materialType)
    {
        return _materials.TryGetValue(materialType, out var material) ? material : null;
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
                for (var i = 0; i < _uberShaderResults.Length; i++)
                    _uberShaderResults[i]?.Dispose();
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
