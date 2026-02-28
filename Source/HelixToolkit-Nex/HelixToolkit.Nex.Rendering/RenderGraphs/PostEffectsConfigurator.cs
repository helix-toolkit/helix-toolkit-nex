using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// Configures a HDR intermediate color target and the final post-effects pass into a <see cref="RenderGraph"/>.
/// <para>
/// Resources declared:
/// <list type="bullet">
///   <item><see cref="SystemBufferNames.TextureColorF16"/> — HDR color render target (Format.RGBA_F16)</item>
///   <item><see cref="SystemBufferNames.FinalOutputTexture"/> — swapchain output; injected each frame from <see cref="RenderContext"/></item>
/// </list>
/// </para>
/// <para>
/// Passes added:
/// <list type="number">
///   <item><see cref="PostEffectsNode"/> — reads HDR color and writes to the final output.</item>
/// </list>
/// </para>
/// </summary>
public static class PostEffectsConfigurator
{
    public static void AddTo(RenderGraph graph)
    {
        graph
            .AddTexture(
                SystemBufferNames.TextureColorF16,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.RGBA_F16,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device
                    )
            )
            // Null builder — the handle is injected each frame from RenderContext.FinalOutputTexture
            .AddFinalOutputTexture()
            .AddPass(
                nameof(PostEffectsNode),
                inputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                outputs: [new(SystemBufferNames.FinalOutputTexture, ResourceType.Texture)],
                onSetup: (res) =>
                {
                    res.Framebuf.Colors[0].Texture = res.Textures[
                        SystemBufferNames.FinalOutputTexture
                    ];
                    res.Pass.Colors[0].ClearColor = Color.Black;
                    res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16];
                }
            );
    }
}
