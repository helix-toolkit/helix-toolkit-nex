using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Manages billboard render pipelines created from <see cref="BillboardMaterialRegistration"/>
/// entries. Each registered billboard material type gets its own compiled fragment shader
/// (with the custom <c>outputColor()</c> implementation) and a corresponding
/// <see cref="RenderPipelineHandle"/>.
/// </summary>
public interface IBillboardMaterialManager : IDisposable
{
    /// <summary>
    /// Gets the number of registered billboard material pipelines.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the render pipeline handle for a given billboard material type.
    /// Returns <see cref="RenderPipelineHandle.Null"/> if the material type is not registered.
    /// </summary>
    RenderPipelineHandle GetPipeline(MaterialTypeId materialId);

    /// <summary>
    /// Creates pipelines for all registered billboard material types in <see cref="BillboardMaterialRegistry"/>.
    /// </summary>
    /// <returns>The number of pipelines created.</returns>
    int CreatePipelinesFromRegistry();

    /// <summary>
    /// Retrieves the unique identifier for a material based on its name.
    /// </summary>
    /// <param name="name">The name of the material. This value cannot be null or empty.</param>
    /// <returns>The <see cref="MaterialTypeId"/> associated with the specified material name.</returns>
    MaterialTypeId? GetMaterialId(string name);

    /// <summary>
    /// Attempts to retrieve the material identifier associated with the specified material name.
    /// </summary>
    /// <param name="name">The name of the material to look up.</param>
    /// <param name="materialId">When this method returns, contains the <see cref="MaterialTypeId"/> associated with the specified material name,
    /// if the lookup was successful; otherwise, contains the default value of <see cref="MaterialTypeId"/>.</param>
    /// <returns><see langword="true"/> if the material identifier was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    bool TryGetMaterialId(string name, out MaterialTypeId materialId);

    /// <summary>
    /// Clear and destroy all materials and their associated GPU pipelines from the manager.
    /// </summary>
    void Clear();
}

/// <summary>
/// Creates and caches billboard render pipelines from <see cref="BillboardMaterialRegistry"/>
/// registrations. Each material type gets its own fragment shader with the custom
/// <c>outputColor()</c> GLSL implementation injected into the billboard template.
/// <para>
/// All pipelines share the same vertex shader (<c>vsBillboard.glsl</c>) and the same
/// render pipeline configuration (triangle strip, no cull, alpha blend + entity ID).
/// Only the fragment shader differs per material type.
/// </para>
/// </summary>
public sealed class BillboardMaterialManager(IContext context, IShaderRepository shaderRepository)
    : IBillboardMaterialManager
{
    private static readonly ILogger _logger = LogManager.Create<BillboardMaterialManager>();

    private readonly IContext _context = context;
    private readonly ConcurrentDictionary<MaterialTypeId, RenderPipelineResource> _pipelines = [];

    private readonly IShaderRepository _shaderRepository = shaderRepository;
    private readonly ShaderCompiler _shaderCompiler = GlslHeaders.CreateCompiler();
    private bool _disposed;

    /// <inheritdoc/>
    public int Count => _pipelines.Count;

    /// <inheritdoc/>
    public RenderPipelineHandle GetPipeline(MaterialTypeId materialId)
    {
        return _pipelines.TryGetValue(materialId, out var pipeline) && pipeline.Valid
            ? pipeline
            : RenderPipelineHandle.Null;
    }

    /// <inheritdoc/>
    public int CreatePipelinesFromRegistry()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var vsResult = _shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Billboard.vsBillboard")
        );

        if (!vsResult.Success)
        {
            _logger.LogError(
                "Failed to compile billboard vertex shader: {Message}",
                vsResult.Errors
            );
            return 0;
        }

        var psResult = BuildUberPipelineForMaterial();

        if (!psResult.Success)
        {
            _logger.LogError(
                "Failed to compile billboard fragment shader: {Message}",
                psResult.Errors
            );
            return 0;
        }

        using var vsModule = _shaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source!
        );

        using var psModule = _shaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            psResult.Source!
        );

        if (!vsModule.Valid || !psModule.Valid)
        {
            _logger.LogError(
                "Failed to load billboard shader modules. Vertex valid={VertexValid}, Fragment valid={FragmentValid}",
                vsModule.Valid,
                psModule.Valid
            );
            return 0;
        }

        int created = 0;
        foreach (var registration in BillboardMaterialRegistry.GetAllRegistrations())
        {
            if (!CreatePipelineForMaterial(registration, vsModule, psModule))
            {
                _logger.LogError(
                    "Failed to create pipeline for billboard material '{Name}' (ID={Id}).",
                    registration.Name,
                    registration.TypeId.Id
                );
            }
            else
            {
                created++;
            }
        }

        return created;
    }

    private ShaderBuildResult BuildUberPipelineForMaterial()
    {
        var fragmentSource = GlslUtils.GetEmbeddedGlslShader("Billboard.psBillboardTemplate");
        fragmentSource = GenerateUberOutputColorFunction(fragmentSource);
        return _shaderCompiler.CompileFragmentShader(fragmentSource);
    }

    private string GenerateUberOutputColorFunction(string template)
    {
        return BillboardMaterialRegistry.GetAllRegistrations().BuildColorOutputImpl(template);
    }

    private bool CreatePipelineForMaterial(
        BillboardMaterialRegistration registration,
        ShaderModuleResource vs,
        ShaderModuleResource fs
    )
    {
        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = $"Billboard_{registration.Name}",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.TriangleStrip,
            DepthFormat = RenderSettings.DepthBufferFormat,
        };

        // Color 0: scene color — premultiplied-alpha blend.
        if (registration.BlendConfig.HasValue)
        {
            pipelineDesc.Colors[0] = registration.BlendConfig.Value;
            pipelineDesc.Colors[0].Format = RenderSettings.IntermediateTargetFormat;
        }
        else
        {
            pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
                RenderSettings.IntermediateTargetFormat
            );
        }

        // Color 1: entity ID (no blend — closest fragment wins via depth test)
        pipelineDesc.Colors[1] = new ColorAttachment
        {
            Format = RenderSettings.MeshIdTexFormat,
            BlendEnabled = false,
        };

        pipelineDesc.SetMaterialType(registration.TypeId);

        var pipeline = _context.CreateRenderPipeline(pipelineDesc);
        if (!pipeline.Valid)
        {
            _logger.LogError(
                "Failed to create render pipeline for billboard material '{Name}'.",
                registration.Name
            );
            return false;
        }

        _pipelines[registration.TypeId] = pipeline;
        _logger.LogInformation(
            "Created billboard material pipeline: {Name} (ID={Id})",
            registration.Name,
            registration.TypeId.Id
        );
        return true;
    }

    /// <inheritdoc/>
    public MaterialTypeId? GetMaterialId(string name)
    {
        return BillboardMaterialRegistry.GetTypeId(name);
    }

    /// <inheritdoc/>
    public bool TryGetMaterialId(string name, out MaterialTypeId materialId)
    {
        return BillboardMaterialRegistry.TryGetTypeId(name, out materialId);
    }

    public void Clear()
    {
        foreach (var pipeline in _pipelines.Values)
            pipeline.Dispose();
        _pipelines.Clear();
    }

    #region IDisposable

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Clear();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
