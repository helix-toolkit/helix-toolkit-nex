using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.RenderGraphs;

/// <summary>
/// Configures the Forward+ light culling compute pass into a <see cref="RenderGraph"/>.
/// <para>
/// Prerequisite: <see cref="DepthPrepassConfigurator"/> must have been applied first, as this
/// configurator consumes <see cref="SystemBufferNames.TextureDepthF32"/> (produced by the depth pass)
/// and <see cref="SystemBufferNames.ForwardPlusConstants"/> (injected from <see cref="RenderContext"/>).
/// </para>
/// <para>
/// Resources declared:
/// <list type="bullet">
///   <item><see cref="SystemBufferNames.ForwardPlusConstants"/> — external (null builder); injected each frame from <see cref="RenderContext"/>.</item>
/// </list>
/// </para>
/// <para>
/// Passes added:
/// <list type="number">
///   <item><see cref="ForwardPlusLightCullingNode"/> — tiled light culling compute dispatch.</item>
/// </list>
/// </para>
/// </summary>
public static class ForwardPlusLightCullingConfigurator
{
    public static void AddTo(RenderGraph graph)
    {
        // ForwardPlusConstants is a system resource injected each frame — no GPU-side builder.
        graph
            .AddBuffer(SystemBufferNames.ForwardPlusConstants, null)
            .AddBuffer(
                SystemBufferNames.BufferLightGrid,
                p =>
                {
                    var totalTiles = p.Context.TileCountX * p.Context.TileCountY;
                    // Light grid buffer: stores light count and index offset per tile
                    return p.Context.Context.CreateBuffer(
                        new BufferDesc
                        {
                            DataSize = (uint)(totalTiles * LightGridTile.SizeInBytes),
                            Usage = BufferUsageBits.Storage,
                            Storage = StorageType.Device,
                        },
                        "FP_LightGrid"
                    );
                }
            )
            .AddBuffer(
                SystemBufferNames.BufferLightIndex,
                p =>
                {
                    var totalTiles = p.Context.TileCountX * p.Context.TileCountY;
                    // Light index list buffer: stores light indices for all tiles
                    return p.Context.Context.CreateBuffer(
                        new BufferDesc
                        {
                            DataSize = (uint)(
                                totalTiles * p.Context.FPLightConfig.MaxLightsPerTile * sizeof(uint)
                            ),
                            Usage = BufferUsageBits.Storage,
                            Storage = StorageType.Device,
                        },
                        "FP_LightIndices"
                    );
                }
            )
            .AddPass(
                nameof(ForwardPlusLightCullingNode),
                inputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
                outputs:
                [
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                ],
                onSetup: (res) =>
                {
                    res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
                }
            );
    }
}
