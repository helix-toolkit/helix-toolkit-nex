namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Fast-Quadric-Mesh-Simplification, port from https://github.com/sp4cerat/Fast-Quadric-Mesh-Simplification
/// </summary>
public sealed class MeshSimplification
{
    private struct SymmetricMatrix
    {
        public const int Size = 10;
        public float M11,
            M12,
            M13,
            M14,
            M22,
            M23,
            M24,
            M33,
            M34,
            M44;

        public SymmetricMatrix(float c = 0)
        {
            M11 = M12 = M13 = M14 = M22 = M23 = M24 = M33 = M34 = M44 = c;
        }

        public SymmetricMatrix(float a, float b, float c, float d)
        {
            M11 = a * a;
            M12 = a * b;
            M13 = a * c;
            M14 = a * d;
            M22 = b * b;
            M23 = b * c;
            M24 = b * d;
            M33 = c * c;
            M34 = c * d;
            M44 = d * d;
        }

        public SymmetricMatrix(
            float m11,
            float m12,
            float m13,
            float m14,
            float m22,
            float m23,
            float m24,
            float m33,
            float m34,
            float m44
        )
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;
            M22 = m22;
            M23 = m23;
            M24 = m24;
            M33 = m33;
            M34 = m34;
            M44 = m44;
        }

        public float this[int c] =>
            c switch
            {
                0 => M11,
                1 => M12,
                2 => M13,
                3 => M14,
                4 => M22,
                5 => M23,
                6 => M24,
                7 => M33,
                8 => M34,
                9 => M44,
                _ => throw new ArgumentOutOfRangeException(nameof(c)),
            };

        public float Det(
            int a11,
            int a12,
            int a13,
            int a21,
            int a22,
            int a23,
            int a31,
            int a32,
            int a33
        )
        {
            var det =
                this[a11] * this[a22] * this[a33]
                + this[a13] * this[a21] * this[a32]
                + this[a12] * this[a23] * this[a31]
                - this[a13] * this[a22] * this[a31]
                - this[a11] * this[a23] * this[a32]
                - this[a12] * this[a21] * this[a33];
            return det;
        }

        public static SymmetricMatrix operator +(SymmetricMatrix n1, SymmetricMatrix n2)
        {
            return new SymmetricMatrix(
                n1[0] + n2[0],
                n1[1] + n2[1],
                n1[2] + n2[2],
                n1[3] + n2[3],
                n1[4] + n2[4],
                n1[5] + n2[5],
                n1[6] + n2[6],
                n1[7] + n2[7],
                n1[8] + n2[8],
                n1[9] + n2[9]
            );
        }

        public void SetAll(float c)
        {
            M11 = M12 = M13 = M14 = M22 = M23 = M24 = M33 = M34 = M44 = c;
        }
    }

    private sealed class Triangle
    {
        public readonly int[] V = new int[3];
        public readonly float[] Err = new float[4];
        public bool Deleted = false;
        public bool Dirty = false;
        public Vector3 Normal = new();

        public Triangle Clone()
        {
            var t = new Triangle()
            {
                Deleted = this.Deleted,
                Dirty = this.Dirty,
                Normal = this.Normal,
            };
            t.V[0] = this.V[0];
            t.V[1] = this.V[1];
            t.V[2] = this.V[2];
            t.Err[0] = this.Err[0];
            t.Err[1] = this.Err[1];
            t.Err[2] = this.Err[2];
            t.Err[3] = this.Err[3];
            return t;
        }
    }

    private sealed class Vertex
    {
        public Vector3 P;
        public int TStart = 0;
        public int TCount = 0;
        public SymmetricMatrix Q = new SymmetricMatrix();
        public bool Border = false;

        public Vertex()
        {
            P = Vector3.Zero;
        }

        public Vertex(Vector3 v)
        {
            P = new Vector3(v.X, v.Y, v.Z);
        }

        public Vertex(ref Vector3 v)
        {
            P = v;
        }

        public Vertex Clone()
        {
            return new Vertex()
            {
                P = this.P,
                Border = this.Border,
                Q = this.Q,
                TCount = this.TCount,
                TStart = this.TStart,
            };
        }
    }

    private struct Ref
    {
        public int Tid;
        public int TVertex;

        public Ref(int id = 0, int tvert = 0)
        {
            Tid = id;
            TVertex = tvert;
        }

        //public Ref Clone()
        //{
        //    return new Ref() { Tid = this.Tid, TVertex = this.TVertex };
        //}

        public void Reset()
        {
            Tid = 0;
            TVertex = 0;
        }
    }

    private readonly List<Triangle> triangles;
    private readonly List<Vertex> vertices;
    private readonly List<Ref> refs;

    /// <summary>
    ///
    /// </summary>
    /// <param name="model"></param>
    public MeshSimplification(MeshGeometry3D? model)
    {
        if (model is null)
        {
            triangles = new();
            vertices = new();
            refs = new();
            return;
        }

        triangles = new List<Triangle>(
            Enumerable.Range(0, model.TriangleIndices.Count / 3).Select(x => new Triangle())
        );
        var i = 0;
        foreach (var tri in triangles)
        {
            tri.V[0] = model.TriangleIndices[i++];
            tri.V[1] = model.TriangleIndices[i++];
            tri.V[2] = model.TriangleIndices[i++];
        }
        vertices = model.Positions.Select(x => new Vertex(x)).ToList();
        refs = new List<Ref>(
            Enumerable.Range(0, model.TriangleIndices.Count).Select(x => new Ref())
        );
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="verbose"></param>
    /// <returns></returns>
    public MeshGeometry3D Simplify(bool verbose = false)
    {
        return Simplify(int.MaxValue, 7, verbose, true);
    }

    /// <summary>
    /// Mesh Simplification using Fast-Quadric-Mesh-Simplification
    /// </summary>
    /// <param name="targetCount">Target Number of Triangles</param>
    /// <param name="aggressive">sharpness to increase the threshold, 5->8 are usually good, more iteration yields higher quality</param>
    /// <param name="verbose"></param>
    /// <param name="lossless"></param>
    /// <returns></returns>
    public MeshGeometry3D Simplify(
        int targetCount,
        float aggressive = 7,
        bool verbose = false,
        bool lossless = false
    )
    {
        foreach (var tri in triangles)
        {
            tri.Deleted = false;
        }
        var deletedTris = 0;
        var deleted0 = new List<bool>();
        var deleted1 = new List<bool>();
        var triCount = triangles.Count;
        var maxIteration = 9999;
        if (!lossless)
        {
            maxIteration = 100;
        }
        for (var iteration = 0; iteration < maxIteration; ++iteration)
        {
            if (!lossless && triCount - deletedTris <= targetCount)
            {
                break;
            }
            if (lossless || iteration % 5 == 0)
            {
                UpdateMesh(iteration);
            }

            foreach (var tri in triangles)
            {
                tri.Dirty = false;
            }
            //
            // All triangles with edges below the threshold will be removed
            //
            // The following numbers works well for most models.
            // If it does not, try to adjust the 3 parameters
            //
            var threshold = 0.001;
            if (!lossless)
                threshold = 0.000000001 * Math.Pow(iteration + 3.0, aggressive);
            if (verbose)
            {
                Debug.WriteLine(
                    $"Iteration: {iteration}; Triangles: {triCount - deletedTris}; Threshold: {threshold};"
                );
            }

            foreach (var tri in triangles)
            {
                if (tri.Err[3] > threshold || tri.Deleted || tri.Dirty)
                {
                    continue;
                }

                for (var j = 0; j < 3; ++j)
                {
                    if (tri.Err[j] < threshold)
                    {
                        var i0 = tri.V[j];
                        var v0 = vertices[i0];
                        var i1 = tri.V[(j + 1) % 3];
                        var v1 = vertices[i1];
                        //Border check
                        if (v0.Border != v1.Border)
                        {
                            continue;
                        }
                        //Compute vertex to collapse to
                        CalculateError(i0, i1, out Vector3 p);
                        deleted0.Clear();
                        deleted1.Clear();
                        deleted0.AddRange(Enumerable.Repeat(false, v0.TCount));
                        deleted1.AddRange(Enumerable.Repeat(false, v1.TCount));

                        if (
                            Flipped(ref p, i0, i1, ref v0, ref v1, deleted0)
                            || Flipped(ref p, i1, i0, ref v1, ref v0, deleted1)
                        )
                        {
                            continue;
                        }
                        v0.P = p;
                        v0.Q = v1.Q + v0.Q;

                        var tStart = refs.Count;
                        UpdateTriangles(i0, ref v0, deleted0, ref deletedTris);
                        UpdateTriangles(i0, ref v1, deleted1, ref deletedTris);

                        var tcount = refs.Count - tStart;
                        if (tcount <= v0.TCount)
                        {
                            if (tcount > 0)
                            {
                                for (var k = 0; k < tcount; ++k)
                                {
                                    refs[v0.TStart + k] = refs[tStart + k];
                                }
                            }
                        }
                        else
                        {
                            v0.TStart = tStart;
                        }

                        v0.TCount = tcount;
                        break;
                    }
                }
                if (!lossless && triCount - deletedTris <= targetCount)
                {
                    break;
                }
            }
            if (lossless)
            {
                if (deletedTris <= 0)
                {
                    break;
                }
                deletedTris = 0;
            }
        }
        CompactMesh();
        return GetMesh();
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public MeshGeometry3D GetMesh()
    {
        var pos = new Vector3Collection(vertices.Select(x => new Vector3(x.P.X, x.P.Y, x.P.Z)));
        var tris = new IntCollection(triangles.Count * 3);
        foreach (var tri in triangles)
        {
            tris.Add(tri.V[0]);
            tris.Add(tri.V[1]);
            tris.Add(tri.V[2]);
        }

        return new MeshGeometry3D() { Positions = pos, TriangleIndices = tris };
    }

    private bool Flipped(
        ref Vector3 p,
        int i0,
        int i1,
        ref Vertex v0,
        ref Vertex v1,
        IList<bool> deleted
    )
    {
        for (var i = 0; i < v0.TCount; ++i)
        {
            var t = triangles[refs[v0.TStart + i].Tid];
            if (t.Deleted)
            {
                continue;
            }
            var s = refs[v0.TStart + i].TVertex;
            var id1 = t.V[(s + 1) % 3];
            var id2 = t.V[(s + 2) % 3];
            if (id1 == i1 || id2 == i1)
            {
                deleted[i] = true;
                continue;
            }

            var d1 = Vector3.Normalize(vertices[id1].P - p);
            var d2 = Vector3.Normalize(vertices[id2].P - p);
            if (Vector3.Dot(d1, d2) > 0.999)
            {
                return true;
            }
            var n = Vector3.Normalize(Vector3.Cross(d1, d2));
            deleted[i] = false;
            if (Vector3.Dot(n, t.Normal) < 0.2)
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateTriangles(
        int i0,
        ref Vertex v,
        IList<bool> deleted,
        ref int deletedTriangles
    )
    {
        for (var i = 0; i < v.TCount; ++i)
        {
            var r = refs[v.TStart + i];
            var t = triangles[r.Tid];
            if (t.Deleted)
            {
                continue;
            }
            if (deleted[i])
            {
                t.Deleted = true;
                deletedTriangles++;
                continue;
            }

            t.V[r.TVertex] = i0;
            t.Dirty = true;
            t.Err[0] = CalculateError(t.V[0], t.V[1], out _);
            t.Err[1] = CalculateError(t.V[1], t.V[2], out _);
            t.Err[2] = CalculateError(t.V[2], t.V[0], out _);
            t.Err[3] = Math.Min(t.Err[0], Math.Min(t.Err[1], t.Err[2]));
            refs.Add(r);
        }
    }

    private float CalculateError(int id_v1, int id_v2, out Vector3 p_result)
    {
        p_result = new Vector3();
        // compute interpolated vertex
        var q = vertices[id_v1].Q + vertices[id_v2].Q;
        var border = vertices[id_v1].Border & vertices[id_v2].Border;
        float error = 0;

        var det = q.Det(0, 1, 2, 1, 4, 5, 2, 5, 7);
        if (det != 0 && !border)
        {
            // q_delta is invertible
            p_result.X = -1 / det * q.Det(1, 2, 3, 4, 5, 6, 5, 7, 8); // vx = A41/det(q_delta)
            p_result.Y = 1 / det * q.Det(0, 2, 3, 1, 5, 6, 2, 7, 8); // vy = A42/det(q_delta)
            p_result.Z = -1 / det * q.Det(0, 1, 3, 1, 4, 6, 2, 5, 8); // vz = A43/det(q_delta)

            error = MeshSimplification.VertexError(ref q, p_result.X, p_result.Y, p_result.Z);
        }
        else
        {
            // det = 0 -> try to find best result
            var p1 = vertices[id_v1].P;
            var p2 = vertices[id_v2].P;
            var p3 = (p1 + p2) / 2;
            var error1 = MeshSimplification.VertexError(ref q, p1.X, p1.Y, p1.Z);
            var error2 = MeshSimplification.VertexError(ref q, p2.X, p2.Y, p2.Z);
            var error3 = MeshSimplification.VertexError(ref q, p3.X, p3.Y, p3.Z);
            error = Math.Min(error1, Math.Min(error2, error3));
            if (error1 == error)
                p_result = p1;
            if (error2 == error)
                p_result = p2;
            if (error3 == error)
                p_result = p3;
        }
        return error;
    }

    private static float VertexError(ref SymmetricMatrix q, float x, float y, float z)
    {
        return q.M11 * x * x
            + 2 * q.M12 * x * y
            + 2 * q.M13 * x * z
            + 2 * q.M14 * x
            + q.M22 * y * y
            + 2 * q.M23 * y * z
            + 2 * q.M24 * y
            + q.M33 * z * z
            + 2 * q.M34 * z
            + q.M44;
    }

    private void UpdateMesh(int iteration)
    {
        if (iteration > 0) // compact triangles
        {
            var dst = 0;
            for (var i = 0; i < triangles.Count; ++i)
            {
                if (!triangles[i].Deleted)
                {
                    triangles[dst++] = triangles[i];
                }
            }
            triangles.RemoveRange(dst, triangles.Count - dst);
        }

        if (iteration == 0)
        {
            foreach (var vert in vertices)
            {
                vert.Q.SetAll(0);
            }

            foreach (var tri in triangles)
            {
                var p0 = vertices[tri.V[0]].P;
                var p1 = vertices[tri.V[1]].P;
                var p2 = vertices[tri.V[2]].P;
                var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                tri.Normal = n;
                for (var j = 0; j < 3; ++j)
                {
                    vertices[tri.V[j]].Q += new SymmetricMatrix(n.X, n.Y, n.Z, -Vector3.Dot(n, p0));
                }
            }

            foreach (var tri in triangles)
            {
                for (var i = 0; i < 3; ++i)
                {
                    tri.Err[i] = CalculateError(tri.V[i], tri.V[(i + 1) % 3], out _);
                }
                tri.Err[3] = Math.Min(tri.Err[0], Math.Min(tri.Err[1], tri.Err[2]));
            }
        }

        foreach (var vert in vertices)
        {
            vert.TStart = 0;
            vert.TCount = 0;
        }

        foreach (var tri in triangles)
        {
            vertices[tri.V[0]].TCount++;
            vertices[tri.V[1]].TCount++;
            vertices[tri.V[2]].TCount++;
        }

        var tstart = 0;
        foreach (var vert in vertices)
        {
            vert.TStart = tstart;
            tstart += vert.TCount;
            vert.TCount = 0;
        }
        var totalTris = triangles.Count * 3;
        if (refs.Count < totalTris)
        {
            refs.Clear();
            refs.AddRange(Enumerable.Range(0, totalTris).Select(x => new Ref()));
        }
        else
        {
            refs.RemoveRange(totalTris, refs.Count - totalTris);
            refs.ForEach(x => x.Reset());
        }
        var count = 0;
        foreach (var tri in triangles)
        {
            for (var j = 0; j < 3; ++j)
            {
                var v = vertices[tri.V[j]];
                var r = refs[v.TStart + v.TCount];
                r.Tid = count;
                r.TVertex = j;
                refs[v.TStart + v.TCount] = r;
                v.TCount++;
            }
            ++count;
        }

        if (iteration == 0)
        {
            var vCount = new IntCollection();
            var vids = new IntCollection();
            foreach (var vert in vertices)
            {
                vert.Border = false;
            }

            foreach (var vert in vertices)
            {
                vCount.Clear();
                vids.Clear();
                for (var j = 0; j < vert.TCount; ++j)
                {
                    var t = triangles[refs[vert.TStart + j].Tid];
                    for (var k = 0; k < 3; ++k)
                    {
                        var ofs = 0;
                        var id = t.V[k];
                        while (ofs < vCount.Count)
                        {
                            if (vids[ofs] == id)
                            {
                                break;
                            }
                            ++ofs;
                        }
                        if (ofs == vCount.Count)
                        {
                            vCount.Add(1);
                            vids.Add(id);
                        }
                        else
                        {
                            vCount[ofs]++;
                        }
                    }
                }

                for (var j = 0; j < vCount.Count; ++j)
                {
                    if (vCount[j] == 1)
                    {
                        vertices[vids[j]].Border = true;
                    }
                }
            }
        }
    }

    private void CompactMesh()
    {
        var dst = 0;
        foreach (var vert in vertices)
        {
            vert.TCount = 0;
        }

        for (var i = 0; i < triangles.Count; ++i)
        {
            if (!triangles[i].Deleted)
            {
                triangles[dst++] = triangles[i];
                vertices[triangles[i].V[0]].TCount = 1;
                vertices[triangles[i].V[1]].TCount = 1;
                vertices[triangles[i].V[2]].TCount = 1;
            }
        }

        triangles.RemoveRange(dst, triangles.Count - dst);
        dst = 0;
        foreach (var vert in vertices)
        {
            if (vert.TCount > 0)
            {
                vert.TStart = dst;
                vertices[dst++].P = vert.P;
            }
        }

        foreach (var tri in triangles)
        {
            tri.V[0] = vertices[tri.V[0]].TStart;
            tri.V[1] = vertices[tri.V[1]].TStart;
            tri.V[2] = vertices[tri.V[2]].TStart;
        }

        vertices.RemoveRange(dst, vertices.Count - dst);
    }
}
