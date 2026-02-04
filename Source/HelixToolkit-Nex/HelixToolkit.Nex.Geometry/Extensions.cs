using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HelixToolkit.Nex.Geometries;

public static class Extensions
{
    public static void UpdateNormalsInPlace(
        this FastList<Vector4> verts,
        FastList<VertexProperties> properties,
        FastList<uint> triangleIndices
    )
    {
        var normals = new Vector3Collection(properties.Count);
        normals.Resize(properties.Count, true);
        var vertArray = verts.GetInternalArray();
        for (var t = 0; t < triangleIndices.Count; t += 3)
        {
            var i1 = (int)triangleIndices[t];
            var i2 = (int)triangleIndices[t + 1];
            var i3 = (int)triangleIndices[t + 2];
            ref var v1 = ref vertArray[i1];
            ref var v2 = ref vertArray[i2];
            ref var v3 = ref vertArray[i3];
            var p1 = (v2 - v1).ToVector3();
            var p2 = (v3 - v1).ToVector3();
            var n = Vector3.Cross(p1, p2);
            // angle
            p1 = Vector3.Normalize(p1);
            p2 = Vector3.Normalize(p2);
            var a = (float)Math.Acos(Vector3.Dot(p1, p2));
            n = Vector3.Normalize(n);
            var v = a * n;
            normals[i1] += v;
            normals[i2] += v;
            normals[i3] += v;
        }
        NormalizeInPlace(normals);
        var propArray = properties.GetInternalArray();
        for (var i = 0; i < properties.Count; ++i)
        {
            propArray[i].Normal = normals[i];
        }
    }

    public static void UpdateNormals(this Geometry geometry)
    {
        geometry.Vertices.UpdateNormalsInPlace(geometry.VertexProps, geometry.Indices);
    }

    public static unsafe void NormalizeInPlace(this FastList<Vector3> data)
    {
#pragma warning disable CS0219
        var elementCount = 3;
#pragma warning restore CS0219
        var currIndex = 0;
#if NET6_0_OR_GREATER
        if (MathSettings.EnableSIMD)
        {
            if (Sse2.IsSupported)
            {
                // Ref: https://virtuallyrandom.com/part-2-vector3-batch-normalization-fpu-vs-simd/
                Debug.Assert(Vector128<float>.Count == 4);
                var inc = Vector128<int>.Count * elementCount;
                var arrayCount = (data.Count - currIndex) * elementCount;
                int length = arrayCount - arrayCount % inc;
                fixed (void* dataPtr = data.GetInternalArray())
                {
                    float* floatData = (float*)dataPtr;
                    floatData += currIndex * elementCount;
                    for (int i = 0; i < length; i += inc, floatData += inc)
                    {
                        var vx = Sse.LoadVector128(floatData);
                        var vy = Sse.LoadVector128(floatData + 4);
                        var vz = Sse.LoadVector128(floatData + 8);
                        // Compute the reciprocal of magnitude: 1 / sqrt(x^2 + y^2 + z^2)
                        var sqX = Sse.Multiply(vx, vx);
                        var sqY = Sse.Multiply(vy, vy);
                        var sqZ = Sse.Multiply(vz, vz);
                        // first transpose
                        //
                        // 0: x0 y0 z0 x1    x0 y0 y1 z1
                        // 1: y1 z1 x2 y2 => z0 x1 x2 y2
                        // 2: z2 x3 y3 z3    z2 x3 y3 z3
                        var xpose1_0 = Sse.MoveLowToHigh(sqX, sqY);
                        var xpose1_1 = Sse.MoveHighToLow(sqY, sqX);
                        // second transpose
                        //
                        // 0: x0 y0 y1 z1    x0 y0 y1 z1
                        // 1: z0 x1 x2 y2 => z0 x1 z2 x3
                        // 2: z2 x3 y3 z3    x2 y2 y3 z3
                        var xpose2_1 = Sse.MoveLowToHigh(xpose1_1, sqZ);
                        var xpose2_2 = Sse.MoveHighToLow(sqZ, xpose1_1);
                        // third transpose
                        // 0: x0 y0 y1 z1    x0 y1 x2 y3
                        // 1: z0 x1 z2 x3 => z0 x1 z2 x3
                        // 2: x2 y2 y3 z3    y0 z1 y2 z3
                        var xpose3_0 = Sse.Shuffle(xpose1_0, xpose2_2, 0b10001000);
                        var xpose3_2 = Sse.Shuffle(xpose1_0, xpose2_2, 0b11011101);

                        var v = Sse.ReciprocalSqrt(Sse.Add(xpose3_2, Sse.Add(xpose3_0, xpose2_1)));

                        // Normalize with reciprocal of magnitude

                        // to apply it, we have to mangle it around again
                        //               s0, s0, s0, s1
                        // x, y, z, w => s1, s1, s2, s2
                        //               s2, s3, s3, s3
                        var scaleX = Sse.Shuffle(v, v, 0b01_00_00_00);
                        var scaleY = Sse.Shuffle(v, v, 0b10_10_01_01);
                        var scaleZ = Sse.Shuffle(v, v, 0b11_11_11_10);

                        vx = Sse.Multiply(vx, scaleX);
                        vy = Sse.Multiply(vy, scaleY);
                        vz = Sse.Multiply(vz, scaleZ);
                        Sse.Store(floatData, vx);
                        Sse.Store(floatData + 4, vy);
                        Sse.Store(floatData + 8, vz);
                    }
                }
                currIndex = length / elementCount;
            }
        }
#endif
        for (var i = currIndex; i < data.Count; i++)
        {
            data[i] = Vector3.Normalize(data[i]);
        }
    }
}
