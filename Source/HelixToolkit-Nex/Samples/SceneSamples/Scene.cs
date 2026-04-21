using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;

namespace SceneSamples;

public interface IScene
{
    int WorldSizeX { get; }
    int WorldSizeZ { get; }
    int MaxTerrainHeight { get; }
    int MinTerrainHeight { get; }
    void RegisterMaterials();
    Node Build(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider
    )
    {
        return BuildAsync(context, resourceManager, worldDataProvider).Result;
    }

    Task<Node> BuildAsync(
        IContext context,
        IResourceManager resourceManager,
        WorldDataProvider worldDataProvider
    );

    void Tick(float deltaTime);
}
