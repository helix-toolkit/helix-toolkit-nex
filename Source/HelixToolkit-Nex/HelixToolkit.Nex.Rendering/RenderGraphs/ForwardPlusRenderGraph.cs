namespace HelixToolkit.Nex.Rendering.RenderGraphs;

public static class ForwardPlusRenderGraph
{
    public static RenderGraph Create(IServiceProvider serviceProvider)
    {
        var graph = new RenderGraph(serviceProvider)
            .AddTexture(
                SystemBufferNames.TextureDepthF32,
                p =>
                    p.Context.CreateTexture2D(
                        Format.Z_F32,
                        (uint)p.ScreenWidth,
                        (uint)p.ScreenHeight,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureMeshId,
                p =>
                    p.Context.CreateTexture2D(
                        Format.RG_F32,
                        (uint)p.ScreenWidth,
                        (uint)p.ScreenHeight,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureColorF16,
                p =>
                    p.Context.CreateTexture2D(
                        Format.RGBA_F16,
                        (uint)p.ScreenWidth,
                        (uint)p.ScreenHeight,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device
                    )
            )
            .AddFinalOutputTexture()
            .AddBuffer(SystemBufferNames.BufferMeshDrawOpaque, null)
            .AddPass(
                nameof(DepthPassNode),
                [new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer)],
                [
                    new(SystemBufferNames.TextureMeshId, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Buffer),
                ],
                (pass, frame, deps, renderContext, buffers, textures) =>
                {
                    frame.DepthStencil.Texture = textures[SystemBufferNames.TextureDepthF32];
                    pass.Depth.ClearDepth = 0.0f;
                    pass.Depth.LoadOp = LoadOp.Clear;
                    pass.Depth.StoreOp = StoreOp.Store;

                    frame.Colors[0].Texture = textures[SystemBufferNames.TextureMeshId];
                    pass.Colors[0].ClearColor = new(0, 0, 0, 0);
                    pass.Colors[0].LoadOp = LoadOp.Clear;
                    pass.Colors[0].StoreOp = StoreOp.Store;
                    if (renderContext.Data is not null)
                    {
                        deps.Buffers[0] = renderContext.Data.MeshDrawsOpaque.Buffer;
                    }
                }
            )
            .AddPass(
                nameof(DebugDepthBufferNode),
                [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
                [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                (pass, frame, deps, renderContext, buffers, textures) =>
                {
                    frame.Colors[0].Texture = textures[SystemBufferNames.TextureColorF16];
                    pass.Colors[0].ClearColor = Color.Coral;
                    pass.Colors[0].LoadOp = LoadOp.Clear;
                    pass.Colors[0].StoreOp = StoreOp.Store;
                    deps.Textures[0] = textures[SystemBufferNames.TextureDepthF32];
                }
            )
            .AddPass(
                nameof(DebugMeshIdNode),
                [new(SystemBufferNames.TextureMeshId, ResourceType.Texture)],
                [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                (pass, frame, deps, renderContext, buffers, textures) =>
                {
                    frame.Colors[0].Texture = textures[SystemBufferNames.TextureColorF16];
                    pass.Colors[0].ClearColor = Color.Black;
                    pass.Colors[0].LoadOp = LoadOp.Clear;
                    pass.Colors[0].StoreOp = StoreOp.Store;
                    deps.Textures[0] = textures[SystemBufferNames.TextureMeshId];
                }
            )
            .AddPass(
                nameof(PostEffectsNode),
                [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                [new(SystemBufferNames.FinalOutputTexture, ResourceType.Texture)],
                (pass, frame, deps, renderContext, buffers, textures) =>
                {
                    frame.Colors[0].Texture = textures[SystemBufferNames.FinalOutputTexture];
                    pass.Colors[0].ClearColor = Color.Black;
                    pass.Colors[0].LoadOp = LoadOp.Clear;
                    pass.Colors[0].StoreOp = StoreOp.Store;
                    deps.Textures[0] = textures[SystemBufferNames.TextureColorF16];
                }
            );
        return graph;
    }
}
