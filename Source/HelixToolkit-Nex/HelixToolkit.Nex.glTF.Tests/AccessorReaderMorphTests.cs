using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using static HelixToolkit.Nex.glTF.Tests.AccessorReaderSkinTests;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Tests for reading morph target deltas (VEC3, quantized and sparse) through <see cref="AccessorReader"/>.
/// </summary>
[TestClass]
public class AccessorReaderMorphTests
{
    private static Accessor Vec3(Accessor.ComponentTypeEnum type, int count, bool normalized = false) =>
        new()
        {
            ComponentType = type,
            Type = Accessor.TypeEnum.VEC3,
            Count = count,
            Normalized = normalized,
        };

    private static byte[] UShorts(params ushort[] values)
    {
        var bytes = new byte[values.Length * sizeof(ushort)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [TestMethod]
    public void ReadVector3s_Float_ReadsValues()
    {
        var reader = CreateReader((Floats(1, 2, 3, 4, 5, 6), Vec3(Accessor.ComponentTypeEnum.FLOAT, 2)));
        var output = new FastList<Vector3>();

        Assert.IsTrue(reader.ReadVector3s(0, output, out var error), error);

        CollectionAssert.AreEqual(new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) }, output.ToArray());
    }

    [TestMethod]
    public void ReadVector3s_NormalizedByte_Dequantizes()
    {
        var reader = CreateReader(
            ([127, 0, 129, 0], Vec3(Accessor.ComponentTypeEnum.BYTE, 1, normalized: true))
        );
        var output = new FastList<Vector3>();

        Assert.IsTrue(reader.ReadVector3s(0, output, out var error), error);

        Assert.AreEqual(new Vector3(1, 0, -1), output[0]);
    }

    [TestMethod]
    public void ReadVector3s_Sparse_ReplacesListedElements()
    {
        // Views 0 and 1 hold the sparse indices and values; the base accessor has no buffer view.
        var (model, reader) = CreateModel(
            (UShorts(2, 0), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 2 }),
            (Floats(7, 8, 9, 1, 2, 3), new Accessor { Type = Accessor.TypeEnum.VEC3, Count = 2 })
        );
        model.Accessors =
        [
            .. model.Accessors,
            new Accessor
            {
                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                Type = Accessor.TypeEnum.VEC3,
                Count = 3,
                Sparse = new AccessorSparse
                {
                    Count = 2,
                    Indices = new AccessorSparseIndices
                    {
                        BufferView = 0,
                        ComponentType = AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_SHORT,
                    },
                    Values = new AccessorSparseValues { BufferView = 1 },
                },
            },
        ];
        reader = new AccessorReader(model, [GetBuffer(reader, model)]);
        var output = new FastList<Vector3>();

        Assert.IsTrue(reader.ReadVector3s(2, output, out var error), error);

        CollectionAssert.AreEqual(
            new[] { new Vector3(1, 2, 3), Vector3.Zero, new Vector3(7, 8, 9) },
            output.ToArray()
        );
    }

    [TestMethod]
    public void ReadVector3s_SparseIndexOutOfRange_Fails()
    {
        var (model, reader) = CreateModel(
            (UShorts(5, 0), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 2 }),
            (Floats(1, 2, 3), new Accessor { Type = Accessor.TypeEnum.VEC3, Count = 1 })
        );
        model.Accessors =
        [
            .. model.Accessors,
            new Accessor
            {
                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                Type = Accessor.TypeEnum.VEC3,
                Count = 2,
                Sparse = new AccessorSparse
                {
                    Count = 1,
                    Indices = new AccessorSparseIndices
                    {
                        BufferView = 0,
                        ComponentType = AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_SHORT,
                    },
                    Values = new AccessorSparseValues { BufferView = 1 },
                },
            },
        ];
        reader = new AccessorReader(model, [GetBuffer(reader, model)]);

        Assert.IsFalse(reader.ReadVector3s(2, new FastList<Vector3>(), out var error));
        StringAssert.Contains(error, "out of range");
    }

    [TestMethod]
    public void ReadVector3s_SparseValuesBeyondTheirView_Fails()
    {
        // The values view holds 4 bytes, but one FLOAT VEC3 needs 12: the following bytes belong
        // to another accessor and must not be read as deltas.
        var (model, reader) = CreateModel(
            (UShorts(0, 0), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 1 }),
            (Floats(1), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 1 }),
            (Floats(9, 9, 9), Vec3(Accessor.ComponentTypeEnum.FLOAT, 1))
        );
        model.Accessors =
        [
            .. model.Accessors,
            new Accessor
            {
                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                Type = Accessor.TypeEnum.VEC3,
                Count = 1,
                Sparse = new AccessorSparse
                {
                    Count = 1,
                    Indices = new AccessorSparseIndices
                    {
                        BufferView = 0,
                        ComponentType = AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_SHORT,
                    },
                    Values = new AccessorSparseValues { BufferView = 1 },
                },
            },
        ];
        reader = new AccessorReader(model, [GetBuffer(reader, model)]);

        Assert.IsFalse(reader.ReadVector3s(3, new FastList<Vector3>(), out var error));
        StringAssert.Contains(error, "exceeds its bounds");
    }

    [TestMethod]
    public void ReadScalars_SparseOnly_ReplacesZeros()
    {
        var (model, reader) = CreateModel(
            (UShorts(3, 1), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 2 }),
            (Floats(0.75f, 0.25f), new Accessor { Type = Accessor.TypeEnum.SCALAR, Count = 2 })
        );
        model.Accessors =
        [
            .. model.Accessors,
            new Accessor
            {
                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                Type = Accessor.TypeEnum.SCALAR,
                Count = 4,
                Sparse = new AccessorSparse
                {
                    Count = 2,
                    Indices = new AccessorSparseIndices
                    {
                        BufferView = 0,
                        ComponentType = AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_SHORT,
                    },
                    Values = new AccessorSparseValues { BufferView = 1 },
                },
            },
        ];
        reader = new AccessorReader(model, [GetBuffer(reader, model)]);

        Assert.IsTrue(reader.ReadScalars(2, out var values, out var error), error);

        CollectionAssert.AreEqual(new[] { 0f, 0.25f, 0f, 0.75f }, values);
    }

    [TestMethod]
    public void ReadVector3s_WrongType_Fails()
    {
        var reader = CreateReader((Floats(1, 2, 3, 4), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1)));

        Assert.IsFalse(reader.ReadVector3s(0, new FastList<Vector3>(), out var error));
        Assert.IsNotNull(error);
    }

    /// <summary>
    /// Returns the single shared buffer of a model built by <see cref="AccessorReaderSkinTests.CreateModel"/>.
    /// </summary>
    private static byte[] GetBuffer(AccessorReader reader, Gltf model)
    {
        return reader.GetBuffer(0) ?? new byte[model.Buffers[0].ByteLength];
    }
}
