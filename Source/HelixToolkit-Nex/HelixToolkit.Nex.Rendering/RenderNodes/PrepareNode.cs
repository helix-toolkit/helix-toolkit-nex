namespace HelixToolkit.Nex.Rendering.RenderNodes;

public class PrepareNode : RenderNode
{
    public override string Name => nameof(PrepareNode);
    public override Color4 DebugColor => Color.Black;

    private RingFixSizeBuffer<FPConstants>? _constantsBuffer;
    private readonly FastList<BufferHandle> _handles = new(5);

    // Fallback sampler for the environment cubemap when the config does not supply one.
    // Linear + clamp with mipmapping so the environment blur (mip LOD) control works.
    private SamplerRef _defaultEnvSampler = SamplerRef.Null;

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null || ResourceManager is null)
            return false;
        _constantsBuffer = new RingFixSizeBuffer<FPConstants>(
            Context,
            (int)GraphicsSettings.MaxFrameInFlight,
            BufferUsageBits.Storage,
            hostVisible: true,
            debugName: "FPConst"
        );
        _defaultEnvSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.LinearClamp.DebugName,
            SamplerStateDesc.LinearClamp
        );
        return true;
    }

    protected override void OnTeardown()
    {
        Disposer.DisposeAndRemove(ref _constantsBuffer);
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        return !(res.RenderContext.Data is null || _constantsBuffer is null);
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        var context = res.RenderContext!;
        _handles.Clear();
        _handles.Add(context.Data!.NodeInfos.Buffer);
        _handles.Add(context.Data!.MeshInfos.Buffer);
        _handles.Add(context.Data!.PBRPropertiesBuffer.Buffer);
        if (context.Data!.DirectionalLights.Count > 0)
        {
            _handles.Add(context.Data!.DirectionalLights.Buffer);
        }
        if (context.Data!.Lights.Count > 0)
        {
            _handles.Add(context.Data!.Lights.Buffer);
        }
        res.CmdBuffer.Barrier(
            _handles.GetInternalArray().AsSpan(0, _handles.Count),
            BarrierPreset.HostWriteToShaderRW
        );

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
            NodeInfoBufferAddress = context.Data!.NodeInfos.GpuAddress,
            MeshInfoBufferAddress = context.Data!.MeshInfos.GpuAddress,
            MaterialBufferAddress = context.Data!.PBRPropertiesBuffer.Buffer.GpuAddress(
                context.Context
            ),
            DirectionalLightsBufferAddress =
                context.Data!.DirectionalLights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferDirectionalLight]
                        .GpuAddress(context.Context)
                    : 0,
            LightBufferAddress = context.Data!.Lights.GpuAddress,
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
            WireframeColor = context.RenderParams.GlobalWireframeColor,
            EnvironmentMap = BuildEnvironmentMapConstants(context.EnvironmentMap),
        };

        _constantsBuffer!.AdvanceAndUpdate(ref fpData);

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
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureEntityId],
            new Color4(0, 0, 0, 0),
            new TextureLayers()
        );
        res.CmdBuffer.ClearDepthStencilImage(res.Textures[SystemBufferNames.TextureDepthF32]);

        // Default TextureColorF16Target to TextureColorF16A so that RenderToFinalNode
        // has a valid source even when PostEffectsNode is absent from the graph.
        res.RenderContext.ResourceSet.Textures[SystemBufferNames.TextureColorF16Target] =
            res.RenderContext.ResourceSet.Textures[SystemBufferNames.TextureColorF16A];
    }

    /// <summary>
    /// Packs the <see cref="EnvironmentMapConfig"/> into the GPU-side sub-struct shared by the
    /// skybox background pass (<see cref="EnvironmentMapNode"/>) and the PBR cubemap reflections.
    /// When no valid cubemap is assigned, <c>RenderCubeMap</c> is forced to 0 so the PBR shader
    /// skips sampling entirely.
    /// </summary>
    private EnvironmentMapConstants BuildEnvironmentMapConstants(EnvironmentMapConfig config)
    {
        if (!config.HasValidTexture)
        {
            return default;
        }

        var samplerIndex = config.Sampler is { Valid: true }
            ? config.Sampler.GetHandle().Index
            : _defaultEnvSampler.GetHandle().Index;

        return new EnvironmentMapConstants
        {
            EnvTexIndex = config.Texture.GetHandle().Index,
            SamplerIndex = samplerIndex,
            Intensity = config.Intensity,
            MipLevel = config.Blur,
            RotationY = config.RotationY,
            RenderCubeMap = config.ShouldRenderCubeMap ? 1u : 0u,
        };
    }

    protected override bool BeginRender(in RenderResources res)
    {
        return true;
    }

    protected override void EndRender(in RenderResources res) { }

    protected override void OnRender(in RenderResources res) { }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferForwardPlusConstants, null)
            .AddTexture(
                SystemBufferNames.TextureColorF16A,
                p =>
                    p.Context.Context.CreateTexture2D(
                        GraphicsSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled
                            | TextureUsageBits.Attachment
                            | TextureUsageBits.InputAttachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureColorF16A
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16B,
                p =>
                    p.Context.Context.CreateTexture2D(
                        GraphicsSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled
                            | TextureUsageBits.Attachment
                            | TextureUsageBits.InputAttachment,
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
            .AddTexture(SystemBufferNames.TextureColorF16Target, null, dependsOnScreenSize: false)
            .AddPass(
                RenderStage.Prepare,
                nameof(PrepareNode),
                inputs: [],
                outputs:
                [
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16A, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16B, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                ]
            );
    }
}
