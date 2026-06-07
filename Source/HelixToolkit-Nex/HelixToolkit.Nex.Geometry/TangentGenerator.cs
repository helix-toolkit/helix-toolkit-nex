namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Generates tangent vectors (with handedness W) for a <see cref="Geometry"/> that
/// has positions, normals, texture coordinates, and triangle indices but no tangents.
/// Uses the Lengyel algorithm (same basis as MikkTSpace) to accumulate per-triangle
/// contributions and compute the handedness sign from the bitangent cross product.
/// </summary>
public static class TangentGenerator
{
    /// <summary>
    /// Computes tangents for <paramref name="geometry"/> in-place.
    /// Does nothing if the geometry lacks normals, UVs, or triangle indices, or if
    /// tangents are already present (i.e. any vertex has a non-zero tangent XYZ).
    /// </summary>
    public static void ComputeTangents(Geometry geometry)
    {
        var vertices = geometry.Vertices;
        var props = geometry.VertexProps;
        var indices = geometry.Indices;

        int vertexCount = vertices.Count;
        int indexCount = indices.Count;

        if (vertexCount == 0 || props.Count != vertexCount || indexCount < 3)
            return;

        // Skip if tangents already present
        for (int i = 0; i < vertexCount; i++)
        {
            var t = props[i].Tangent;
            if (t.X != 0 || t.Y != 0 || t.Z != 0)
                return;
        }

        var tan1 = new Vector3[vertexCount];
        var tan2 = new Vector3[vertexCount];

        for (int t = 0; t < indexCount; t += 3)
        {
            int i1 = (int)indices[t];
            int i2 = (int)indices[t + 1];
            int i3 = (int)indices[t + 2];

            if (i1 >= vertexCount || i2 >= vertexCount || i3 >= vertexCount)
                continue;

            var v1 = new Vector3(vertices[i1].X, vertices[i1].Y, vertices[i1].Z);
            var v2 = new Vector3(vertices[i2].X, vertices[i2].Y, vertices[i2].Z);
            var v3 = new Vector3(vertices[i3].X, vertices[i3].Y, vertices[i3].Z);

            var uv1 = props[i1].TexCoord;
            var uv2 = props[i2].TexCoord;
            var uv3 = props[i3].TexCoord;

            var edge1 = v2 - v1;
            var edge2 = v3 - v1;
            var deltaUV1 = uv2 - uv1;
            var deltaUV2 = uv3 - uv1;

            float denom = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            // Skip degenerate triangles (zero UV area)
            if (MathF.Abs(denom) < 1e-8f)
                continue;

            float r = 1.0f / denom;

            var sDir = new Vector3(
                (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X) * r,
                (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y) * r,
                (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z) * r
            );
            var tDir = new Vector3(
                (deltaUV1.X * edge2.X - deltaUV2.X * edge1.X) * r,
                (deltaUV1.X * edge2.Y - deltaUV2.X * edge1.Y) * r,
                (deltaUV1.X * edge2.Z - deltaUV2.X * edge1.Z) * r
            );

            tan1[i1] += sDir;
            tan1[i2] += sDir;
            tan1[i3] += sDir;
            tan2[i1] += tDir;
            tan2[i2] += tDir;
            tan2[i3] += tDir;
        }

        for (int i = 0; i < vertexCount; i++)
        {
            var prop = props[i];
            var n = prop.Normal;
            var t = tan1[i];

            // Gram-Schmidt orthogonalize
            var tangentXYZ = Vector3.Normalize(t - n * Vector3.Dot(n, t));

            // Handedness: if cross(N,T) and tan2 point in opposite directions, flip
            float w = Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f ? -1.0f : 1.0f;

            prop.Tangent = new Vector4(tangentXYZ, w);
            props[i] = prop;
        }
    }
}
