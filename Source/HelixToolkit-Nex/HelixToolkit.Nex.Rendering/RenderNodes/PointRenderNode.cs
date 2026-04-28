namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class PointRenderNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<PointRenderNode>();

    public override string Name => nameof(PointRenderNode);
    public override Color4 DebugColor => Color.Cornsilk;

    #region Setup / Teardown

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null)
        {
            _logger.LogError("Context or Renderer is null during PointRenderNode setup.");
            return false;
        }
        return true;
    }
    #endregion

    #region Render

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.Context;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null. Skipping point rendering.");
            return false;
        }

        if (context.Data.PointCloudData!.TotalPointCount == 0)
        {
            return false;
        }
        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
        if (!fpBuffer.Valid)
        {
            return false;
        }
        var fpData = new FPConstants
        {
            Enabled = 1u,
            TimeMs = res.Context.TimeMs,
            CameraPosition = context.CameraParams.Position,
            InverseViewProjection = context.CameraParams.InvViewProjection,
            ViewProjection = context.CameraParams.ViewProjection,
            View = context.CameraParams.View,
            InverseView = context.CameraParams.InvView,
            ScreenDimensions = new Vector2(context.WindowSize.Width, context.WindowSize.Height),
            DpiScale = context.DpiScale,
            DirectionalLightsBufferAddress =
                context.Data.DirectionalLights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferDirectionalLight]
                        .GpuAddress(context.Context)
                    : 0,
            LightBufferAddress = context.Data.Lights.GpuAddress,
            LightGridBufferAddress =
                context.Data.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightGrid].GpuAddress(context.Context)
                    : 0,
            LightIndexBufferAddress =
                context.Data.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightIndex].GpuAddress(context.Context)
                    : 0,
            TileCountX = (uint)context.TileCountX,
            TileCountY = (uint)context.TileCountY,
            LightCount = (uint)context.Data.Lights.Count,
            TileSize = context.FPLightConfig.TileSize,
            MaxLightsPerTile = context.FPLightConfig.MaxLightsPerTile,
        };
        res.CmdBuffer.UpdateBuffer(fpBuffer, ref fpData);
        return base.BeginRender(res);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context is null || res.Context.Data is null)
        {
            _logger.LogWarning("Point data is null. Skipping point culling.");
            return;
        }
        var points = res.Context.Data.PointCloudData;
        foreach (var entry in points!.Data.Values)
        {
            if (entry.PointCount == 0)
            {
                continue;
            }
            var pipeline = res.Context.Data.ResourceManager.PointMaterialManager.GetPipeline(
                entry.MaterialId
            );
            Debug.Assert(pipeline.Valid, "Point render pipeline is not valid.");

            var fpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.Context.Context);

            res.CmdBuffer.BindRenderPipeline(pipeline);
            res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);

            res.CmdBuffer.PushConstants(
                new PointRenderPC
                {
                    DrawDataAddress = entry.DrawDataBuffer,
                    FpConstAddress = fpConstAddress,
                }
            );

            res.CmdBuffer.DrawIndirect(
                entry.DrawArgsBuffer,
                0,
                1,
                PointDrawIndirectArgs.SizeInBytes
            );
        }
    }
    #endregion

    #region Render Graph

    public override void AddToGraph(RenderGraph graph)
    {
        // Register point-specific GPU buffers
        graph.AddPass(
            nameof(PointRenderNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            onSetup: (res) =>
            {
                // Color 0: scene color (load existing opaque content)
                res.Framebuf.Colors[0].Texture = res.Textures[
                    SystemBufferNames.TextureColorF16Current
                ];
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;

                // Color 1: entity ID (load existing mesh IDs, overwrite where points are closer)
                res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
                res.Pass.Colors[1].LoadOp = LoadOp.Load;
                res.Pass.Colors[1].StoreOp = StoreOp.Store;

                // Depth: read + write (points depth-test against meshes AND each other)
                res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Pass.Depth.LoadOp = LoadOp.Load;
                res.Pass.Depth.StoreOp = StoreOp.Store;

                // Dependencies
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Current];
                res.Deps.Textures[1] = res.Textures[SystemBufferNames.TextureDepthF32];
            },
            stage: RenderStage.Opaque,
            after: [nameof(ForwardPlusOpaqueNode)]
        );
    }

    #endregion
}
