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
                [],
                [
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16A, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16B, ResourceType.Texture),
                    new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture),
                ],
                res => { }
            );
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16A],
            BackgroundColor,
            new TextureLayers()
        );
        res.CmdBuffer.ClearColorImage(
            res.Textures[SystemBufferNames.TextureColorF16B],
            BackgroundColor,
            new TextureLayers()
        );

        // Default TextureColorF16Current to TextureColorF16Target so that RenderToFinalNode
        // has a valid source even when PostEffectsNode is absent from the graph.
        if (res.Context.ResourceSet is { } resourceSet)
        {
            resourceSet.Textures[SystemBufferNames.TextureColorF16Current] = resourceSet.Textures[
                SystemBufferNames.TextureColorF16A
            ];
        }
    }

    protected override bool BeginRender(in RenderResources res)
    {
        return true;
    }

    protected override void EndRender(in RenderResources res) { }

    protected override bool OnSetup()
    {
        return true;
    }
}
