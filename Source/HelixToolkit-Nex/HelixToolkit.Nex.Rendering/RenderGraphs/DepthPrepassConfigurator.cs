using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// Configures the depth pre-pass stage into a <see cref="RenderGraph"/>.
/// <para>
/// Resources declared:
/// <list type="bullet">
///   <item><see cref="SystemBufferNames.TextureDepthF32"/> — device-side depth attachment (Format.Z_F32)</item>
///   <item><see cref="SystemBufferNames.TextureMeshId"/> — device-side mesh-id attachment (Format.RG_F32)</item>
///   <item><see cref="SystemBufferNames.BufferMeshDrawOpaque"/> — external (null builder); injected each frame from <see cref="RenderContext"/></item>
/// </list>
/// </para>
/// <para>
/// Passes added:
/// <list type="number">
///   <item><see cref="FrustumCullNode"/> — produces opaque and transparent draw lists.</item>
///   <item><see cref="DepthPassNode"/> — writes depth and mesh-id.</item>
/// </list>
/// </para>
/// </summary>
public static class DepthPrepassConfigurator
{
    public static void AddTo(RenderGraph graph)
    {
        graph
            .AddTexture(
                SystemBufferNames.TextureDepthF32,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.Z_F32,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device
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
                        StorageType.Device
                    )
            )
            // Null builder — the handle is injected each frame from RenderContext.Data
            .AddBuffer(SystemBufferNames.BufferMeshDrawOpaque, null)
            .AddBuffer(SystemBufferNames.BufferMeshDrawTransparent, null)
            .AddBuffer(
                SystemBufferNames.ForwardPlusConstants,
                p =>
                    p.Context.Context.CreateBuffer(
                        new FPConstants(),
                        BufferUsageBits.Storage,
                        StorageType.Device
                    )
            )
            .AddPass(
                nameof(FrustumCullNode),
                inputs: [],
                outputs:
                [
                    new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
                    new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
                ],
                onSetup: (_) => { }
            )
            .AddPass(
                nameof(DepthPassNode),
                inputs: [new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer)],
                outputs:
                [
                    new(SystemBufferNames.TextureMeshId, ResourceType.Texture),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                ],
                onSetup: (res) =>
                {
                    res.Framebuf.DepthStencil.Texture = res.Textures[
                        SystemBufferNames.TextureDepthF32
                    ];
                    res.Pass.Depth.ClearDepth = 0.0f;
                    res.Pass.Depth.LoadOp = LoadOp.Clear;
                    res.Pass.Depth.StoreOp = StoreOp.Store;

                    res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureMeshId];
                    res.Pass.Colors[0].ClearColor = new(0, 0, 0, 0);
                    res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;

                    res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawOpaque];
                }
            );
    }
}
