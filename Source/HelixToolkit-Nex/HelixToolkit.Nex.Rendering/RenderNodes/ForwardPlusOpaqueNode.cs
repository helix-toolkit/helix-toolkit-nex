namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class ForwardPlusOpaqueNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusOpaqueNode>();
    public override string Name => nameof(ForwardPlusOpaqueNode);

    public override Color4 DebugColor => Color.Green;

    public bool UseLightCulling { set; get; } = true;

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.Context;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping forward+ opaque pass.");
            return false;
        }
        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
        if (!fpBuffer.Valid)
        {
            return false;
        }
        var fpData = new FPConstants
        {
            Enabled = UseLightCulling ? 1u : 0,
            Time = res.Context.Time,
            CameraPosition = context.CameraParams.Position,
            InverseViewProjection = context.CameraParams.InvViewProjection,
            ViewProjection = context.CameraParams.ViewProjection,
            ScreenDimensions = new Vector2(context.WindowSize.Width, context.WindowSize.Height),
            DpiScale = context.DpiScale,
            MeshInfoBufferAddress = context.Data.MeshInfos.GpuAddress,
            MeshDrawBufferAddress = context.Data.MeshDrawsOpaque.GpuAddress,
            MaterialBufferAddress = context.Data.PBRPropertiesBuffer.Buffer.GpuAddress(
                context.Context
            ),
            DirectionalLightsBufferAddress =
                context.Data.DirectionalLights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferDirectionalLight]
                        .GpuAddress(context.Context)
                    : 0,
            LightBufferAddress = context.Data.Lights.GpuAddress,
            LightGridBufferAddress =
                context.Data?.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightGrid].GpuAddress(context.Context)
                    : 0,
            LightIndexBufferAddress =
                context.Data?.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightIndex].GpuAddress(context.Context)
                    : 0,
            TileCountX = (uint)context.TileCountX,
            TileCountY = (uint)context.TileCountY,
            LightCount = (uint)context.Data!.Lights.Count,
            TileSize = context.FPLightConfig.TileSize,
            MaxLightsPerTile = context.FPLightConfig.MaxLightsPerTile,
        };
        res.CmdBuffer.UpdateBuffer(fpBuffer, fpData);
        return base.BeginRender(in res);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping depth pass.");
            return;
        }
        if (res.Context.Data.MeshDrawsOpaque.Count > 0)
        {
            res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
            res.Context.Statistics.DrawCalls += RenderHelper.RenderOpaque(in res);
        }
    }

    protected override bool OnSetup()
    {
        return true;
    }

    protected override void OnTeardown()
    {
        base.OnTeardown();
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferDirectionalLight, null)
            .AddBuffer(SystemBufferNames.BufferLights, null)
            .AddPass(
                nameof(ForwardPlusOpaqueNode),
                inputs:
                [
                    new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                ],
                outputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                onSetup: (res) =>
                {
                    res.Framebuf.DepthStencil.Texture = res.Textures[
                        SystemBufferNames.TextureDepthF32
                    ];
                    res.Pass.Depth.LoadOp = LoadOp.Load;
                    res.Pass.Depth.StoreOp = StoreOp.DontCare;

                    res.Framebuf.Colors[0].Texture = res.Textures[
                        SystemBufferNames.TextureColorF16
                    ];
                    res.Pass.Colors[0].LoadOp = LoadOp.Load;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
                    res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawOpaque];
                    res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
                    res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferLightGrid];
                    res.Deps.Buffers[3] = res.Buffers[SystemBufferNames.BufferLightIndex];
                }
            );
    }
}
