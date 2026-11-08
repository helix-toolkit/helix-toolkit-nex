namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Specifies the shape drawn at line segment ends that are not connected to another segment.
/// Values match the <c>lineCap</c> field of the GPU <c>LineDraw</c> record.
/// </summary>
public enum LineCap : uint
{
    /// <summary>
    /// The line ends flat exactly at the endpoint.
    /// </summary>
    Butt = 0,

    /// <summary>
    /// The line ends flat, extended beyond the endpoint by half the line width.
    /// </summary>
    Square = 1,

    /// <summary>
    /// The line ends with a semicircle centered on the endpoint.
    /// </summary>
    Round = 2,
}
