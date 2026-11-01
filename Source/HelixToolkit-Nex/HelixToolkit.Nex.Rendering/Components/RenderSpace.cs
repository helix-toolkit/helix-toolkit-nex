namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Specifies the space in which a draw primitive's size/thickness is measured.
/// </summary>
public enum RenderSpace : uint
{
    /// <summary>
    /// The size/thickness is a fixed screen-space pixel measurement, independent of camera
    /// distance (no perspective scaling).
    /// </summary>
    Screen = 0,

    /// <summary>
    /// The size/thickness is a physical world-space measurement that is projected to screen
    /// space, so it appears smaller/thinner as the camera moves away.
    /// </summary>
    World = 1,
}
