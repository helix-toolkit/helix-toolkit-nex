namespace HelixToolkit.Nex.Rendering.ComputeNodes;

/// <summary>
/// A compute render node that performs tiled Forward+ light culling.
/// <para>
/// This node reads the depth buffer (produced by a preceding <see cref="RenderNodes.DepthPassNode"/>)
/// and the Forward+ constants from <see cref="RenderContext"/>, then dispatches the light culling
/// compute shader. The results are written into the light-grid and light-index buffers managed by
/// <see cref="RenderContext"/>.
/// </para>
/// <para>
/// Expected <see cref="Dependencies"/> layout supplied by the render graph:
/// <list type="bullet">
///   <item>Textures[0] — depth buffer (<see cref="SystemBufferNames.TextureDepthF32"/>)</item>
/// </list>
/// </para>
/// </summary>
public sealed class ForwardPlusLightCullingNode : ComputeNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusLightCullingNode>();

    private ComputePipelineResource _pipeline = ComputePipelineResource.Null;
    private RingFixSizeBuffer<LightCullingConstants>? _cullingConstantsBuffer;
    private LightCullingConstants _cullingConstants;
    private SamplerRef _depthSampler = SamplerRef.Null;
    private uint _lightCountLogLimiter = 0;

    public override string Name => nameof(ForwardPlusLightCullingNode);
    public override Color4 DebugColor => Color.Gold;

    public bool EnableAABBCulling { get; set; } = true;

    public bool EnableDepthMaskCulling { get; set; } = true;

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError(
                "Context or ResourceManager is null, cannot set up ForwardPlusLightCullingNode."
            );
            return false;
        }

        _cullingConstantsBuffer = new(
            Context,
            (int)GraphicsSettings.MaxFrameInFlight,
            debugName: "FP_LightCull"
        );
        _depthSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.PointClamp.DebugName,
            SamplerStateDesc.PointClamp
        );

        if (!_depthSampler.Valid)
        {
            _logger.LogError("Failed to create depth sampler for ForwardPlusLightCullingNode.");
            return false;
        }

        return CreatePipeline();
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        Disposer.DisposeAndRemove(ref _cullingConstantsBuffer);
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        if (res.RenderContext.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping light culling pass.");
            return false;
        }
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        var renderContext = res.RenderContext;

        if (renderContext.Data.Lights.Count == 0)
        {
            // No lights, no need to dispatch the compute shader
            return false;
        }
        return true;
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLights]);
        var renderContext = res.RenderContext!;
        var tileCountX = (uint)renderContext.TileCountX;
        var tileCountY = (uint)renderContext.TileCountY;

        _cullingConstants.ViewMatrix = renderContext.CameraParams.View;
        _cullingConstants.Projection = renderContext.CameraParams.Projection;
        _cullingConstants.InverseProjection = renderContext.CameraParams.InvProjection;
        _cullingConstants.ScreenDimensions = new Vector2(
            renderContext.WindowSize.Width,
            renderContext.WindowSize.Height
        );
        _cullingConstants.TileCountX = tileCountX;
        _cullingConstants.TileCountY = tileCountY;
        // Constrain the global range-light set to the 16-bit range before culling so that every
        // stored light index fits in a ushort (<= 65535). The cull shader writes one light index
        // per stored entry, so capping the iterated light count guarantees no stored index can
        // exceed 65535. If the scene exceeds the limit we keep the first 65535 lights and warn,
        // rather than silently producing out-of-range indices.
        if (renderContext.Data!.Lights.Count > Limits.MaxRangeLightCount && _lightCountLogLimiter++ % 128 == 0)
        {
            _logger.LogWarning(
                "Scene has {LightCount} lights, exceeding the Forward+ culling limit of {MaxLights}." +
                " Capping to the first {MaxLights} lights, which may cause incorrect rendering.",
                renderContext.Data.Lights.Count,
                Limits.MaxRangeLightCount,
                Limits.MaxRangeLightCount
            );
        }

        var sceneLightCount = Math.Min(Limits.MaxRangeLightCount, renderContext.Data!.Lights.Count);
        _cullingConstants.LightCount = sceneLightCount;
        _cullingConstants.ZNear = renderContext.CameraParams.NearPlane;
        _cullingConstants.ZFar = renderContext.CameraParams.FarPlane;
        _cullingConstants.DepthTextureIndex = res.Textures[SystemBufferNames.TextureDepthF32].Index;
        _cullingConstants.MaxLightsPerTile = renderContext.FPLightConfig.MaxLightsPerTile;
        _cullingConstants.LightBufferAddress = renderContext.Data.Lights.GpuAddress;
        _cullingConstants.SamplerIndex = _depthSampler;
        _cullingConstants.LightGridBufferAddress = res.Buffers[SystemBufferNames.BufferLightGrid]
            .GpuAddress(renderContext.Context);
        _cullingConstants.LightIndexBufferAddress = res.Buffers[SystemBufferNames.BufferLightIndex]
            .GpuAddress(renderContext.Context);
        _cullingConstants.EnableAABBCulling = EnableAABBCulling ? 1u : 0u;
        _cullingConstants.EnableDepthMaskCulling = EnableDepthMaskCulling ? 1u : 0u;
        _cullingConstantsBuffer!.AdvanceAndUpdate(ref _cullingConstants);
        res.CmdBuffer.Barrier(_cullingConstantsBuffer.Current);
    }

    protected override void OnRender(in RenderResources res)
    {
        var renderContext = res.RenderContext!;
        var tileCountX = (uint)renderContext.TileCountX;
        var tileCountY = (uint)renderContext.TileCountY;
        res.CmdBuffer.BindComputePipeline(_pipeline);
        res.CmdBuffer.PushConstants(_cullingConstantsBuffer!.GpuAddress);
        res.CmdBuffer.DispatchThreadGroups(new Dimensions(tileCountX, tileCountY, 1), res.Deps);
    }

    private bool CreatePipeline()
    {
        if (Context is null)
        {
            return false;
        }

        var shaderSource = ForwardPlusLightCulling.GenerateComputeShader(
            new ForwardPlusLightCulling.Config()
        );

        using var shaderModule = Context.CreateShaderModuleGlsl(
            shaderSource,
            ShaderStage.Compute,
            "FP_LightCull"
        );

        _pipeline = Context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = shaderModule }
        );

        Debug.Assert(_pipeline.Valid, "Failed to create Forward+ light culling compute pipeline.");
        return _pipeline.Valid;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            // BufferLights is created and managed elsewhere (e.g., by the RenderContext); register it
            // here for dependency tracking only, so no local factory is supplied.
            .AddBuffer(SystemBufferNames.BufferLights, null)
            .AddBuffer(
                SystemBufferNames.BufferLightGrid,
                p =>
                {
                    var totalTiles = p.Context.TileCountX * p.Context.TileCountY;
                    // Light grid buffer: stores light count and index offset per tile
                    return p.Context.Context.CreateBuffer(
                        new BufferDesc
                        {
                            DataSize = (uint)(totalTiles * LightGridTile.SizeInBytes),
                            Usage = BufferUsageBits.Storage,
                            Storage = StorageType.Device,
                        },
                        SystemBufferNames.BufferLightGrid
                    );
                }
            )
            .AddBuffer(
                SystemBufferNames.BufferLightIndex,
                p =>
                {
                    var totalTiles = p.Context.TileCountX * p.Context.TileCountY;
                    // Light index list buffer: stores light indices for all tiles
                    return p.Context.Context.CreateBuffer(
                        new BufferDesc
                        {
                            DataSize = (
                                Alignment.GetAlignedSize(
                                    (uint)totalTiles
                                        * p.Context.FPLightConfig.MaxLightsPerTile
                                        * sizeof(ushort),
                                    32
                                )
                            ),
                            Usage = BufferUsageBits.Storage,
                            Storage = StorageType.Device,
                        },
                        SystemBufferNames.BufferLightIndex
                    );
                }
            )
            .AddPass(
                RenderStage.Prepare,
                nameof(ForwardPlusLightCullingNode),
                inputs:
                [
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.BufferLights, ResourceType.Buffer),
                ],
                outputs:
                [
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                ]
            );
    }
}
