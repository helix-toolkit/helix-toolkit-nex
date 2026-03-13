namespace HelixToolkit.Nex.Rendering.RenderNodes;

public class PrepareNode : RenderNode
{
    public override string Name => nameof(PrepareNode);
    public override Color4 DebugColor => Color.Black;

    public Color4 BackgroundColor { set; get; } = Color.Transparent;

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(
                SystemBufferNames.BufferForwardPlusConstants,
                p =>
                    p.Context.Context.CreateBuffer(
                        new FPConstants(),
                        BufferUsageBits.Storage,
                        StorageType.Device,
                        SystemBufferNames.BufferForwardPlusConstants
                    ),
                dependsOnScreenSize: false
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16Target,
                p =>
                    p.Context.Context.CreateTexture2D(
                        RenderSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: "TexColorF16A"
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16Sample,
                p =>
                    p.Context.Context.CreateTexture2D(
                        RenderSettings.IntermediateTargetFormat,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: "TexColorF16B"
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
            .AddPass(
                nameof(PrepareNode),
                [],
                [
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16Sample, ResourceType.Texture),
                ],
                res => { }
            );
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16Target],
            BackgroundColor,
            new TextureLayers()
        );
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16Sample],
            BackgroundColor,
            new TextureLayers()
        );
    }

    protected override bool BeginRender(in RenderResources res)
    {
        // Compute nodes do not begin a render pass.
        res.CmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        return true;
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.PopDebugGroupLabel();
        // Compute nodes do not end a render pass.
    }

    protected override bool OnSetup()
    {
        return true;
    }
}
