using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Generates flat (per-face) normals for a triangle-list <see cref="Geometry"/> that has no normals,
/// as required by glTF for primitives without a <c>NORMAL</c> attribute.
/// </summary>
public static class NormalGenerator
{
    /// <summary>
    /// Computes flat normals for <paramref name="geometry"/> in-place. Each triangle gets its own
    /// three vertices, so an indexed geometry is unwelded first: vertices, vertex properties, vertex
    /// colors, joint influences and morph target deltas are duplicated per triangle corner and the
    /// indices become sequential. Normals follow the counter-clockwise winding of the triangle indices.
    /// </summary>
    /// <remarks>
    /// Does nothing unless the topology is <see cref="Topology.Triangle"/> and the geometry has at
    /// least one triangle. Existing texture coordinates and tangents are kept; vertex properties are
    /// created when the geometry has none. Degenerate triangles get a zero normal.
    /// </remarks>
    /// <param name="geometry">The geometry to update.</param>
    public static void ComputeFlatNormals(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Topology != Topology.Triangle)
        {
            return;
        }
        var vertexCount = geometry.Vertices.Count;
        if (geometry.Indices.Count > 0)
        {
            if (geometry.Indices.Count < 3)
            {
                return;
            }
            Unweld(geometry);
        }
        else if (vertexCount < 3)
        {
            return;
        }

        var vertices = geometry.Vertices;
        var props = geometry.VertexProps;
        if (props.Count != vertices.Count)
        {
            props.Clear();
            for (var i = 0; i < vertices.Count; ++i)
            {
                props.Add(VertexProperties.Empty);
            }
        }
        var propArray = props.GetInternalArray();
        for (var i = 0; i + 2 < vertices.Count; i += 3)
        {
            var p0 = vertices[i].ToVector3();
            var normal = Vector3.Cross(vertices[i + 1].ToVector3() - p0, vertices[i + 2].ToVector3() - p0);
            var lengthSquared = normal.LengthSquared();
            normal = lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : Vector3.Zero;
            propArray[i].Normal = normal;
            propArray[i + 1].Normal = normal;
            propArray[i + 2].Normal = normal;
        }
        geometry.HasFlatNormals = true;
    }

    private static void Unweld(Geometry geometry)
    {
        var indices = geometry.Indices;
        var vertexCount = geometry.Vertices.Count;
        var cornerCount = indices.Count - indices.Count % 3;

        var vertices = new FastList<Vector4>(cornerCount);
        var props = CreatePerCorner(geometry.VertexProps, vertexCount, cornerCount);
        var colors = CreatePerCorner(geometry.VertexColors, vertexCount, cornerCount);
        var influences = CreatePerCorner(geometry.JointInfluences, vertexCount, cornerCount);
        var newIndices = new FastList<uint>(cornerCount);
        var morphTargets = new FastList<MorphTarget>(geometry.MorphTargets.Count);
        foreach (var target in geometry.MorphTargets)
        {
            morphTargets.Add(new MorphTarget(target.Name));
        }

        for (var t = 0; t < cornerCount; t += 3)
        {
            var i0 = indices[t];
            var i1 = indices[t + 1];
            var i2 = indices[t + 2];
            // Drop triangles that reference missing vertices.
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
            {
                continue;
            }
            AddCorner(geometry, (int)i0, vertices, props, colors, influences, morphTargets, newIndices);
            AddCorner(geometry, (int)i1, vertices, props, colors, influences, morphTargets, newIndices);
            AddCorner(geometry, (int)i2, vertices, props, colors, influences, morphTargets, newIndices);
        }

        geometry.Vertices = vertices;
        geometry.VertexProps = props ?? [];
        if (colors is not null)
        {
            geometry.VertexColors = colors;
        }
        if (influences is not null)
        {
            geometry.JointInfluences = influences;
        }
        if (morphTargets.Count > 0)
        {
            geometry.MorphTargets = morphTargets;
        }
        geometry.Indices = newIndices;
    }

    private static void AddCorner(
        Geometry geometry,
        int index,
        FastList<Vector4> vertices,
        FastList<VertexProperties>? props,
        FastList<Vector4>? colors,
        FastList<GpuJointInfluence>? influences,
        FastList<MorphTarget> morphTargets,
        FastList<uint> indices
    )
    {
        indices.Add((uint)vertices.Count);
        vertices.Add(geometry.Vertices[index]);
        props?.Add(geometry.VertexProps[index]);
        colors?.Add(geometry.VertexColors[index]);
        influences?.Add(geometry.JointInfluences[index]);
        var vertexCount = geometry.Vertices.Count;
        for (var t = 0; t < morphTargets.Count; ++t)
        {
            var source = geometry.MorphTargets[t];
            var target = morphTargets[t];
            AddDelta(source.PositionDeltas, target.PositionDeltas, index, vertexCount);
            AddDelta(source.NormalDeltas, target.NormalDeltas, index, vertexCount);
            AddDelta(source.TangentDeltas, target.TangentDeltas, index, vertexCount);
        }
    }

    /// <summary>
    /// Copies the delta of one corner when <paramref name="source"/> has one delta per vertex.
    /// </summary>
    private static void AddDelta(FastList<Vector3> source, FastList<Vector3> target, int index, int vertexCount)
    {
        if (source.Count == vertexCount)
        {
            target.Add(source[index]);
        }
    }

    /// <summary>
    /// Returns an empty list to collect per-corner values of <paramref name="source"/>, or
    /// <see langword="null"/> when the attribute is not per-vertex.
    /// </summary>
    private static FastList<T>? CreatePerCorner<T>(FastList<T> source, int vertexCount, int cornerCount)
    {
        return source.Count == vertexCount ? new FastList<T>(cornerCount) : null;
    }
}
