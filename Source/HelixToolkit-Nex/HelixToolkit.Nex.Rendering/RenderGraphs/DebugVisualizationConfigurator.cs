using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// Configures debug visualization passes that display the depth buffer and mesh-id buffer
/// as color images into <see cref="SystemBufferNames.TextureColorF16"/>.
/// <para>
/// Prerequisite: <see cref="DepthPrepassConfigurator"/> must have been applied first, as
/// this configurator consumes <see cref="SystemBufferNames.TextureDepthF32"/> and
/// <see cref="SystemBufferNames.TextureMeshId"/>.
/// </para>
/// <para>
/// Passes added:
/// <list type="number">
///   <item><see cref="DebugDepthBufferNode"/> — visualizes the depth buffer.</item>
///   <item><see cref="DebugMeshIdNode"/> — visualizes the mesh-id buffer.</item>
/// </list>
/// </para>
/// </summary>
public static class DebugVisualizationConfigurator
{
    public static void AddTo(RenderGraph graph)
    {
        graph
            .AddPass(
                nameof(DebugDepthBufferNode),
                inputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
                outputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                onSetup: (res) =>
                {
                    res.Framebuf.Colors[0].Texture = res.Textures[
                        SystemBufferNames.TextureColorF16
                    ];
                    res.Pass.Colors[0].ClearColor = Color.Coral;
                    res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
                }
            )
            .AddPass(
                nameof(DebugMeshIdNode),
                inputs: [new(SystemBufferNames.TextureMeshId, ResourceType.Texture)],
                outputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
                onSetup: (res) =>
                {
                    res.Framebuf.Colors[0].Texture = res.Textures[
                        SystemBufferNames.TextureColorF16
                    ];
                    res.Pass.Colors[0].ClearColor = Color.Black;
                    res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureMeshId];
                }
            );
    }
}
