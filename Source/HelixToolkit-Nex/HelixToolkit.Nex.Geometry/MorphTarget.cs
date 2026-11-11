namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// A morph target (blend shape) of a <see cref="Geometry"/>: per-vertex displacements that are
/// added to the base vertices, scaled by the target's weight (a glTF primitive <c>target</c>).
/// </summary>
/// <remarks>
/// Each delta list is either empty (the attribute is not displaced) or has exactly one entry per
/// vertex of the owning geometry. Call <see cref="Geometry.MarkDirty"/> with
/// <see cref="GeometryBufferType.Morph"/> after changing the deltas of an uploaded geometry.
/// </remarks>
public sealed class MorphTarget
{
    /// <summary>
    /// Gets or sets the target name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the position displacements, one per vertex, or empty.
    /// </summary>
    public FastList<Vector3> PositionDeltas { get; } = [];

    /// <summary>
    /// Gets the normal displacements, one per vertex, or empty.
    /// </summary>
    public FastList<Vector3> NormalDeltas { get; } = [];

    /// <summary>
    /// Gets the tangent (XYZ) displacements, one per vertex, or empty.
    /// </summary>
    public FastList<Vector3> TangentDeltas { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MorphTarget"/> class.
    /// </summary>
    /// <param name="name">Optional target name.</param>
    public MorphTarget(string? name = null)
    {
        Name = name ?? string.Empty;
    }

    /// <summary>
    /// Creates a copy of the target with its own delta lists.
    /// </summary>
    public MorphTarget Clone()
    {
        var clone = new MorphTarget(Name);
        clone.PositionDeltas.AddRange(PositionDeltas);
        clone.NormalDeltas.AddRange(NormalDeltas);
        clone.TangentDeltas.AddRange(TangentDeltas);
        return clone;
    }

    /// <summary>
    /// Returns <see langword="true"/> when every non-empty delta list has <paramref name="vertexCount"/> entries.
    /// </summary>
    public bool IsValidFor(int vertexCount)
    {
        return IsValid(PositionDeltas, vertexCount)
            && IsValid(NormalDeltas, vertexCount)
            && IsValid(TangentDeltas, vertexCount);
    }

    private static bool IsValid(FastList<Vector3> deltas, int vertexCount)
    {
        return deltas.Count == 0 || deltas.Count == vertexCount;
    }
}
