namespace HelixToolkit.Nex.Rendering;

public enum RenderStages
{
    None = 0,
    Begin,
    FrustumCull,
    DepthPrePass,
    LightCull,
    Opaque,
    ScreenSpace,
    Transparent,
    PostEffect,
    Overlay,
    UI,
    Composition,
    End,
}

public enum ObjectTypes
{
    Opaque,
    Transparent,
    Points,
    Lines,
    Billboard,
    Particle,
}
