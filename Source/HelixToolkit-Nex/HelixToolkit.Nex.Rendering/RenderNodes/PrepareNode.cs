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
                SystemBufferNames.ForwardPlusConstants,
                p =>
                    p.Context.Context.CreateBuffer(
                        new FPConstants(),
                        BufferUsageBits.Storage,
                        StorageType.Device,
                        "FPConstants"
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.RGBA_F16,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: "ColorF16"
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
                        debugName: "DepthF32"
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureMeshId,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.RG_F32,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: "MeshId"
                    )
            )
            .AddFinalOutputTexture()
            .AddPass(
                nameof(PrepareNode),
                [],
                [
                    new(SystemBufferNames.ForwardPlusConstants, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.TextureMeshId, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16, ResourceType.Texture),
                ],
                res => { }
            );
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16],
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
