namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Specifies how connected line segments are joined. Two consecutive segments in a line list are
/// connected when the end vertex of the first equals the start vertex of the second; the last and
/// first segments are also connected when they meet, closing the loop.
/// Values match the <c>lineJoin</c> field of the GPU <c>LineDraw</c> record.
/// </summary>
public enum LineJoin : uint
{
    /// <summary>
    /// Segments are rendered independently; every segment end uses the <see cref="LineCap"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// The outer edges are extended until they meet. Joints whose miter would exceed the miter
    /// limit fall back to <see cref="Bevel"/>.
    /// </summary>
    Miter = 1,

    /// <summary>
    /// The outer corners are connected by a straight edge.
    /// </summary>
    Bevel = 2,

    /// <summary>
    /// The outer corners are connected by a circular arc centered on the joint.
    /// </summary>
    Round = 3,
}
