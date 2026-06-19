namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// The output of the Draco decoder: per-semantic decoded vertex attribute arrays,
/// a shared vertex count, and an optional index array.
/// </summary>
/// <remarks>
/// The decoder guarantees the following structural invariants (Requirement 2.3):
/// every <see cref="DecodedAttribute.ElementCount"/> in <see cref="Attributes"/>
/// equals <see cref="VertexCount"/>, and <see cref="Indices"/> is either
/// <see langword="null"/> (non-indexed geometry) or exactly one index array.
/// </remarks>
internal sealed class DecodedMesh
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedMesh"/> class.
    /// </summary>
    /// <param name="vertexCount">The shared vertex count across all decoded attributes.</param>
    /// <param name="attributes">The decoded attributes keyed by glTF semantic name.</param>
    /// <param name="indices">The decoded index array, or <see langword="null"/> for non-indexed geometry.</param>
    public DecodedMesh(
        int vertexCount,
        IReadOnlyDictionary<string, DecodedAttribute> attributes,
        uint[]? indices
    )
    {
        VertexCount = vertexCount;
        Attributes = attributes;
        Indices = indices;
    }

    /// <summary>
    /// Gets the number of vertices shared by every decoded attribute.
    /// </summary>
    public int VertexCount { get; }

    /// <summary>
    /// Gets the decoded attributes keyed by glTF semantic name (for example
    /// <c>POSITION</c>, <c>NORMAL</c>, <c>TEXCOORD_0</c>).
    /// </summary>
    public IReadOnlyDictionary<string, DecodedAttribute> Attributes { get; }

    /// <summary>
    /// Gets the decoded index array, or <see langword="null"/> when the mesh is
    /// non-indexed (Requirement 3.5).
    /// </summary>
    public uint[]? Indices { get; }
}
