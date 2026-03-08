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
    private BufferResource _cullingConstantsBuffer = BufferResource.Null;
    private LightCullingConstants _cullingConstants;
    private SamplerResource _depthSampler = SamplerResource.Null;

    public override string Name => nameof(ForwardPlusLightCullingNode);
    public override Color4 DebugColor => Color.Gold;

    public bool EnableAABBCulling { get; set; } = true;

    public bool EnableDepthMaskCulling { get; set; } = true;

    protected override bool OnSetup()
    {
        if (Context is null)
        {
            _logger.LogError("Context is null, cannot set up ForwardPlusLightCullingNode.");
            return false;
        }

        _cullingConstantsBuffer = Context.CreateBuffer(
            _cullingConstants,
            BufferUsageBits.Storage,
            StorageType.Device,
            "FP_LightCull"
        );
        _depthSampler = Context.CreateSampler(SamplerStateDesc.PointClamp);

        return CreatePipeline();
    }

    protected override void OnTeardown()
    {
        _depthSampler.Dispose();
        _pipeline.Dispose();
        _cullingConstantsBuffer.Dispose();
        base.OnTeardown();
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping light culling pass.");
            return;
        }
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        var renderContext = res.Context;

        if (renderContext.Data.Lights.Count == 0)
        {
            // No lights, no need to dispatch the compute shader
            return;
        }
        var tileCountX = (uint)renderContext.TileCountX;
        var tileCountY = (uint)renderContext.TileCountY;

        _cullingConstants.ViewMatrix = renderContext.CameraParams.View;
        _cullingConstants.Projection = renderContext.CameraParams.Projection;
        _cullingConstants.ScreenDimensions = new Vector2(
            renderContext.WindowSize.Width,
            renderContext.WindowSize.Height
        );
        _cullingConstants.TileCountX = tileCountX;
        _cullingConstants.TileCountY = tileCountY;
        _cullingConstants.LightCount = (uint)(renderContext.Data.Lights.Count);
        _cullingConstants.ZNear = renderContext.CameraParams.NearPlane;
        _cullingConstants.ZFar = renderContext.CameraParams.FarPlane;
        _cullingConstants.DepthTextureIndex = res.Textures[SystemBufferNames.TextureDepthF32].Index;
        _cullingConstants.MaxLightsPerTile = renderContext.FPLightConfig.MaxLightsPerTile;
        _cullingConstants.LightBufferAddress = renderContext.Data.Lights.GpuAddress;
        _cullingConstants.SamplerIndex = _depthSampler.Index;
        _cullingConstants.LightGridBufferAddress = res.Buffers[SystemBufferNames.BufferLightGrid]
            .GpuAddress(renderContext.Context);
        _cullingConstants.LightIndexBufferAddress = res.Buffers[SystemBufferNames.BufferLightIndex]
            .GpuAddress(renderContext.Context);
        _cullingConstants.EnableAABBCulling = EnableAABBCulling ? 1u : 0u;
        _cullingConstants.EnableDepthMaskCulling = EnableDepthMaskCulling ? 1u : 0u;
        res.CmdBuffer.UpdateBuffer(_cullingConstantsBuffer, _cullingConstants);
        res.CmdBuffer.BindComputePipeline(_pipeline);
        res.CmdBuffer.PushConstants(_cullingConstantsBuffer.GpuAddress);
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
                        "FP_LightGrid"
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
                            DataSize = (uint)(
                                totalTiles * p.Context.FPLightConfig.MaxLightsPerTile * sizeof(uint)
                            ),
                            Usage = BufferUsageBits.Storage,
                            Storage = StorageType.Device,
                        },
                        "FP_LightIndices"
                    );
                }
            )
            .AddPass(
                nameof(ForwardPlusLightCullingNode),
                inputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
                outputs:
                [
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                ],
                onSetup: (res) =>
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32]
            );
    }
}
