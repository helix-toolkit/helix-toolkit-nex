using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using Vertex = System.Numerics.Vector4;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based tests for AccessorReader (Properties 4, 5, 6).
/// </summary>
[TestClass]
public class AccessorReaderPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Feature: gltf-importer, Property 4: Accessor data fidelity (positions)

    /// <summary>
    /// Property 4: For any array of N 3D position vectors encoded as a glTF accessor (FLOAT, VEC3),
    /// reading via AccessorReader SHALL produce exactly N Vertex entries where each entry's (x, y, z)
    /// matches the source data within floating-point tolerance and w == 1.0.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [TestMethod]
    public void ReadPositions_ProducesCorrectVertexEntries_ForAnyFloatVec3Data()
    {
        // Generate random arrays of N Vector3 positions (1..200 positions)
        var positionsGen =
            from count in Gen.Choose(1, 200)
            from floats in Gen.ArrayOf(
                Gen.Choose(-1000000, 1000000).Select(v => v / 100.0f),
                count * 3
            )
            select (count, floats);

        Prop.ForAll(
                Arb.From(positionsGen),
                ((int count, float[] floats) input) =>
                {
                    int count = input.count;
                    float[] sourceFloats = input.floats;

                    // Encode positions into a byte buffer (3 floats per position, 4 bytes per float)
                    var byteBuffer = new byte[count * 3 * sizeof(float)];
                    System.Buffer.BlockCopy(sourceFloats, 0, byteBuffer, 0, byteBuffer.Length);

                    // Create a mock glTF model with accessor (VEC3, FLOAT)
                    var model = new Gltf
                    {
                        Accessors =
                        [
                            new Accessor
                            {
                                BufferView = 0,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                                Type = Accessor.TypeEnum.VEC3,
                                Count = count,
                            },
                        ],
                        BufferViews =
                        [
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = 0,
                                ByteLength = byteBuffer.Length,
                            },
                        ],
                        Buffers = [new glTFLoader.Schema.Buffer { ByteLength = byteBuffer.Length }],
                    };

                    var reader = new AccessorReader(model, [byteBuffer]);
                    var output = new FastList<Vertex>();

                    // Act
                    reader.ReadPositions(0, output);

                    // Assert: output count == N
                    if (output.Count != count)
                        return false;

                    // Assert: each entry's (x,y,z) matches input within tolerance, w == 1.0
                    for (int i = 0; i < count; i++)
                    {
                        float expectedX = sourceFloats[i * 3];
                        float expectedY = sourceFloats[i * 3 + 1];
                        float expectedZ = sourceFloats[i * 3 + 2];

                        var vertex = output[i];

                        if (MathF.Abs(vertex.X - expectedX) > 1e-5f)
                            return false;
                        if (MathF.Abs(vertex.Y - expectedY) > 1e-5f)
                            return false;
                        if (MathF.Abs(vertex.Z - expectedZ) > 1e-5f)
                            return false;
                        if (vertex.W != 1.0f)
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    // Feature: gltf-importer, Property 6: Index data preservation

    /// <summary>
    /// Property 6: For any array of index values encoded as a glTF accessor
    /// (UNSIGNED_SHORT, SCALAR), reading via AccessorReader.ReadIndices SHALL produce
    /// a uint list with identical values (widened from ushort) and identical count.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void ReadIndices_UnsignedShort_PreservesValues()
    {
        // Generate random arrays of ushort values (1..500 elements)
        var indicesGen =
            from count in Gen.Choose(1, 500)
            from values in Gen.ArrayOf(Gen.Choose(0, ushort.MaxValue).Select(v => (ushort)v), count)
            select values;

        Prop.ForAll(
                Arb.From(indicesGen),
                (ushort[] sourceIndices) =>
                {
                    // Encode as UNSIGNED_SHORT SCALAR accessor
                    var byteBuffer = new byte[sourceIndices.Length * 2];
                    for (int i = 0; i < sourceIndices.Length; i++)
                    {
                        BitConverter.TryWriteBytes(byteBuffer.AsSpan(i * 2, 2), sourceIndices[i]);
                    }

                    var model = new Gltf
                    {
                        Accessors =
                        [
                            new Accessor
                            {
                                BufferView = 0,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
                                Type = Accessor.TypeEnum.SCALAR,
                                Count = sourceIndices.Length,
                            },
                        ],
                        BufferViews =
                        [
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = 0,
                                ByteLength = byteBuffer.Length,
                            },
                        ],
                        Buffers = [new glTFLoader.Schema.Buffer { ByteLength = byteBuffer.Length }],
                    };

                    var reader = new AccessorReader(model, [byteBuffer]);
                    var output = new FastList<uint>();
                    reader.ReadIndices(0, output);

                    // Verify count matches
                    if (output.Count != sourceIndices.Length)
                        return false;

                    // Verify each value matches (widened to uint)
                    for (int i = 0; i < sourceIndices.Length; i++)
                    {
                        if (output[i] != (uint)sourceIndices[i])
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 6: For any array of index values encoded as a glTF accessor
    /// (UNSIGNED_INT, SCALAR), reading via AccessorReader.ReadIndices SHALL produce
    /// a uint list with identical values and identical count.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void ReadIndices_UnsignedInt_PreservesValues()
    {
        // Generate random arrays of uint values (1..500 elements)
        var indicesGen =
            from count in Gen.Choose(1, 500)
            from values in Gen.ArrayOf(Gen.Choose(0, int.MaxValue).Select(v => (uint)v), count)
            select values;

        Prop.ForAll(
                Arb.From(indicesGen),
                (uint[] sourceIndices) =>
                {
                    // Encode as UNSIGNED_INT SCALAR accessor
                    var byteBuffer = new byte[sourceIndices.Length * 4];
                    for (int i = 0; i < sourceIndices.Length; i++)
                    {
                        BitConverter.TryWriteBytes(byteBuffer.AsSpan(i * 4, 4), sourceIndices[i]);
                    }

                    var model = new Gltf
                    {
                        Accessors =
                        [
                            new Accessor
                            {
                                BufferView = 0,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_INT,
                                Type = Accessor.TypeEnum.SCALAR,
                                Count = sourceIndices.Length,
                            },
                        ],
                        BufferViews =
                        [
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = 0,
                                ByteLength = byteBuffer.Length,
                            },
                        ],
                        Buffers = [new glTFLoader.Schema.Buffer { ByteLength = byteBuffer.Length }],
                    };

                    var reader = new AccessorReader(model, [byteBuffer]);
                    var output = new FastList<uint>();
                    reader.ReadIndices(0, output);

                    // Verify count matches
                    if (output.Count != sourceIndices.Length)
                        return false;

                    // Verify each value matches exactly
                    for (int i = 0; i < sourceIndices.Length; i++)
                    {
                        if (output[i] != sourceIndices[i])
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    // Feature: gltf-importer, Property 5: Vertex attribute count invariant

    /// <summary>
    /// Property 5: For any mesh primitive with POSITION and one or more additional attributes
    /// (NORMAL, TEXCOORD_0, TANGENT, COLOR_0), the resulting Geometry SHALL have
    /// VertexProps.Count == Vertices.Count and (if COLOR_0 present) VertexColors.Count == Vertices.Count.
    /// **Validates: Requirements 3.2, 3.3, 3.4, 3.6**
    /// </summary>
    [TestMethod]
    public void AllVertexAttributes_HaveSameCount_AsPositions()
    {
        // Generator: random vertex count N (1..100) with random subset of additional attributes
        var primitiveGen =
            from vertexCount in Gen.Choose(1, 100)
            from hasNormal in Gen.Elements(true, false)
            from hasTexCoord in Gen.Elements(true, false)
            from hasTangent in Gen.Elements(true, false)
            from hasColor in Gen.Elements(true, false)
            from colorIsVec4 in Gen.Elements(true, false)
                // Ensure at least one additional attribute is present
            let effectiveHasNormal = hasNormal || (!hasTexCoord && !hasTangent && !hasColor)
            select (
                vertexCount,
                effectiveHasNormal,
                hasTexCoord,
                hasTangent,
                hasColor,
                colorIsVec4
            );

        Prop.ForAll(
                Arb.From(primitiveGen),
                (
                    (
                        int vertexCount,
                        bool hasNormal,
                        bool hasTexCoord,
                        bool hasTangent,
                        bool hasColor,
                        bool colorIsVec4
                    ) input
                ) =>
                {
                    int N = input.vertexCount;
                    bool hasNormal = input.hasNormal;
                    bool hasTexCoord = input.hasTexCoord;
                    bool hasTangent = input.hasTangent;
                    bool hasColor = input.hasColor;
                    bool colorIsVec4 = input.colorIsVec4;

                    // Calculate buffer sizes
                    int positionBytes = N * 3 * sizeof(float); // VEC3 FLOAT
                    int normalBytes = hasNormal ? N * 3 * sizeof(float) : 0; // VEC3 FLOAT
                    int texCoordBytes = hasTexCoord ? N * 2 * sizeof(float) : 0; // VEC2 FLOAT
                    int tangentBytes = hasTangent ? N * 4 * sizeof(float) : 0; // VEC4 FLOAT
                    int colorComponents = colorIsVec4 ? 4 : 3;
                    int colorBytes = hasColor ? N * colorComponents * sizeof(float) : 0; // VEC3 or VEC4 FLOAT

                    int totalBytes =
                        positionBytes + normalBytes + texCoordBytes + tangentBytes + colorBytes;
                    var byteBuffer = new byte[totalBytes];

                    // Fill buffer with arbitrary float data (values don't matter for count invariant)
                    var rng = new Random(
                        N * 31
                            + (hasNormal ? 1 : 0)
                            + (hasTexCoord ? 2 : 0)
                            + (hasTangent ? 4 : 0)
                            + (hasColor ? 8 : 0)
                    );
                    for (int i = 0; i < totalBytes / sizeof(float); i++)
                    {
                        float val = (float)(rng.NextDouble() * 2.0 - 1.0);
                        BitConverter.TryWriteBytes(
                            byteBuffer.AsSpan(i * sizeof(float), sizeof(float)),
                            val
                        );
                    }

                    // Build accessors and buffer views
                    var accessors = new List<Accessor>();
                    var bufferViews = new List<BufferView>();
                    int currentOffset = 0;

                    // Accessor 0: POSITION (VEC3 FLOAT)
                    accessors.Add(
                        new Accessor
                        {
                            BufferView = bufferViews.Count,
                            ByteOffset = 0,
                            ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                            Type = Accessor.TypeEnum.VEC3,
                            Count = N,
                        }
                    );
                    bufferViews.Add(
                        new BufferView
                        {
                            Buffer = 0,
                            ByteOffset = currentOffset,
                            ByteLength = positionBytes,
                        }
                    );
                    currentOffset += positionBytes;

                    int normalAccessorIndex = -1;
                    if (hasNormal)
                    {
                        normalAccessorIndex = accessors.Count;
                        accessors.Add(
                            new Accessor
                            {
                                BufferView = bufferViews.Count,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                                Type = Accessor.TypeEnum.VEC3,
                                Count = N,
                            }
                        );
                        bufferViews.Add(
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = currentOffset,
                                ByteLength = normalBytes,
                            }
                        );
                        currentOffset += normalBytes;
                    }

                    int texCoordAccessorIndex = -1;
                    if (hasTexCoord)
                    {
                        texCoordAccessorIndex = accessors.Count;
                        accessors.Add(
                            new Accessor
                            {
                                BufferView = bufferViews.Count,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                                Type = Accessor.TypeEnum.VEC2,
                                Count = N,
                            }
                        );
                        bufferViews.Add(
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = currentOffset,
                                ByteLength = texCoordBytes,
                            }
                        );
                        currentOffset += texCoordBytes;
                    }

                    int tangentAccessorIndex = -1;
                    if (hasTangent)
                    {
                        tangentAccessorIndex = accessors.Count;
                        accessors.Add(
                            new Accessor
                            {
                                BufferView = bufferViews.Count,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                                Type = Accessor.TypeEnum.VEC4,
                                Count = N,
                            }
                        );
                        bufferViews.Add(
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = currentOffset,
                                ByteLength = tangentBytes,
                            }
                        );
                        currentOffset += tangentBytes;
                    }

                    int colorAccessorIndex = -1;
                    if (hasColor)
                    {
                        colorAccessorIndex = accessors.Count;
                        accessors.Add(
                            new Accessor
                            {
                                BufferView = bufferViews.Count,
                                ByteOffset = 0,
                                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                                Type = colorIsVec4
                                    ? Accessor.TypeEnum.VEC4
                                    : Accessor.TypeEnum.VEC3,
                                Count = N,
                            }
                        );
                        bufferViews.Add(
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = currentOffset,
                                ByteLength = colorBytes,
                            }
                        );
                        currentOffset += colorBytes;
                    }

                    var model = new Gltf
                    {
                        Accessors = accessors.ToArray(),
                        BufferViews = bufferViews.ToArray(),
                        Buffers = [new glTFLoader.Schema.Buffer { ByteLength = totalBytes }],
                    };

                    var reader = new AccessorReader(model, [byteBuffer]);

                    // Read positions
                    var positions = new FastList<Vertex>();
                    reader.ReadPositions(0, positions);
                    if (positions.Count != N)
                        return false;

                    // Read normals (merge=false then merge=true) and verify count == N
                    if (hasNormal)
                    {
                        // Test merge=false: adds new entries
                        var propsNoMerge = new FastList<VertexProperties>();
                        reader.ReadNormals(normalAccessorIndex, propsNoMerge, merge: false);
                        if (propsNoMerge.Count != N)
                            return false;

                        // Test merge=true: updates existing entries
                        var propsForMerge = new FastList<VertexProperties>();
                        for (int i = 0; i < N; i++)
                            propsForMerge.Add(new VertexProperties());
                        reader.ReadNormals(normalAccessorIndex, propsForMerge, merge: true);
                        if (propsForMerge.Count != N)
                            return false;
                    }

                    // Read tex coords and verify count == N
                    if (hasTexCoord)
                    {
                        var propsTexCoord = new FastList<VertexProperties>();
                        reader.ReadTexCoords(texCoordAccessorIndex, propsTexCoord, merge: false);
                        if (propsTexCoord.Count != N)
                            return false;
                    }

                    // Read tangents and verify count == N
                    if (hasTangent)
                    {
                        var propsTangent = new FastList<VertexProperties>();
                        reader.ReadTangents(tangentAccessorIndex, propsTangent, merge: false);
                        if (propsTangent.Count != N)
                            return false;
                    }

                    // Read colors and verify count == N
                    if (hasColor)
                    {
                        var colors = new FastList<Vector4>();
                        reader.ReadColors(colorAccessorIndex, colors);
                        if (colors.Count != N)
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
