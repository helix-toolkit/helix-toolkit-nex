namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// A single decoded vertex attribute produced by the Draco decoder. Holds the
/// flattened per-vertex values together with the number of components per vertex
/// (for example 3 for <c>POSITION</c>/<c>NORMAL</c>, 2 for <c>TEXCOORD_0</c>,
/// 4 for <c>TANGENT</c>/<c>COLOR_0</c>).
/// </summary>
/// <param name="Values">
/// The flattened attribute values laid out as <c>ElementCount * Components</c>
/// floats in vertex order.
/// </param>
/// <param name="Components">The number of float components per vertex.</param>
internal readonly record struct DecodedAttribute(float[] Values, int Components)
{
    /// <summary>
    /// Gets the number of vertices represented by this attribute, computed as
    /// <c>Values.Length / Components</c>. Returns 0 when <see cref="Components"/> is 0.
    /// </summary>
    public int ElementCount => Components == 0 ? 0 : Values.Length / Components;
}
