namespace HelixToolkit.Nex.Rendering.RenderNodes;

public class PrepareNode : RenderNode
{
    public override string Name => nameof(PrepareNode);
    public override Color4 DebugColor => Color.Black;

    private RingFixSizeBuffer<FPConstants>? _constantsBuffer;

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null)
            return false;
        _constantsBuffer = new RingFixSizeBuffer<FPConstants>(
            Context,
            (int)RenderSettings.NumFrameInFlight(Context),
            BufferUsageBits.Storage,
            hostVisible: false,
            debugName: "FPConst"
        );
        return true;
    }

    protected override void OnTeardown()
    {
        Disposer.DisposeAndRemove(ref _constantsBuffer);
        base.OnTeardown();
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferForwardPlusConstants, null)
            .AddTexture(
                SystemBufferNames.TextureColorF16A,
                p =>
                    p.Context.Context.CreateTexture2D(
                        RenderSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureColorF16A
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16B,
                p =>
                    p.Context.Context.CreateTexture2D(
                        RenderSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureColorF16B
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureDepthF32,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.Z_F32,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureDepthF32
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureEntityId,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.RG_F32,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureEntityId
                    )
            )
            .AddFinalOutputTexture()
            // Register the stable current-color alias with no build function —
            // its handle is set at runtime by PostEffectsNode (or PrepareNode as a fallback).
            .AddTexture(SystemBufferNames.TextureColorF16Current, null, dependsOnScreenSize: false)
            .AddPass(
                nameof(PrepareNode),
                inputs: [],
                outputs:
                [
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16A, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16B, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture),
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                ],
                stage: RenderStage.Prepare
            );
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        if (res.RenderContext.Data is null || _constantsBuffer is null)
        {
            return;
        }

        var context = res.RenderContext!;
        var fpData = new FPConstants
        {
            Enabled = context.RenderParams.EnableLightCulling ? 1u : 0,
            TimeMs = context.TimeMs,
            CameraPosition = context.CameraParams.Position,
            InverseViewProjection = context.CameraParams.InvViewProjection,
            ViewProjection = context.CameraParams.ViewProjection,
            View = context.CameraParams.View,
            InverseView = context.CameraParams.InvView,
            ScreenDimensions = new Vector2(context.WindowSize.Width, context.WindowSize.Height),
            DpiScale = context.DpiScale,
            MeshInfoBufferAddress = context.Data.MeshInfos.GpuAddress,
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
            PointerRing = context.PointerRing,
        };

        _constantsBuffer.AdvanceAndUpdate(ref fpData);

        res.Buffers[SystemBufferNames.BufferForwardPlusConstants] = _constantsBuffer!.Current;

        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16A],
            res.RenderContext.RenderParams.BackgroundColor,
            new TextureLayers()
        );
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16B],
            res.RenderContext.RenderParams.BackgroundColor,
            new TextureLayers()
        );

        // Default TextureColorF16Current to TextureColorF16Target so that RenderToFinalNode
        // has a valid source even when PostEffectsNode is absent from the graph.
        if (res.RenderContext.ResourceSet is { } resourceSet)
        {
            resourceSet.Textures[SystemBufferNames.TextureColorF16Current] = resourceSet.Textures[
                SystemBufferNames.TextureColorF16A
            ];
        }
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.ClearDepth = 0f;
        res.Pass.Depth.LoadOp = LoadOp.Clear;
        res.Pass.Depth.StoreOp = StoreOp.Store;
    }

    protected override void OnRender(in RenderResources res) { }
}
