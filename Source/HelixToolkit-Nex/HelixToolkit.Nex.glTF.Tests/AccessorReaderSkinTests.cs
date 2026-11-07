using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Tests for reading skinning data (JOINTS_0, WEIGHTS_0, inverse bind matrices) through
/// <see cref="AccessorReader"/>.
/// </summary>
[TestClass]
public class AccessorReaderSkinTests
{
    /// <summary>
    /// Builds a model whose accessors each live in their own buffer view over a shared buffer.
    /// </summary>
    internal static AccessorReader CreateReader(params (byte[] Data, Accessor Accessor)[] entries)
    {
        return CreateModel(entries).Reader;
    }

    /// <summary>
    /// Builds a model whose accessors each live in their own buffer view over a shared buffer, and
    /// a reader over it.
    /// </summary>
    internal static (Gltf Model, AccessorReader Reader) CreateModel(
        params (byte[] Data, Accessor Accessor)[] entries
    )
    {
        var buffer = new List<byte>();
        var accessors = new List<Accessor>();
        var views = new List<BufferView>();
        foreach (var (data, accessor) in entries)
        {
            // Keep every view 4-byte aligned, as glTF requires.
            while (buffer.Count % 4 != 0)
            {
                buffer.Add(0);
            }
            views.Add(
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = buffer.Count,
                    ByteLength = data.Length,
                }
            );
            accessor.BufferView = views.Count - 1;
            accessors.Add(accessor);
            buffer.AddRange(data);
        }
        var model = new Gltf
        {
            Accessors = [.. accessors],
            BufferViews = [.. views],
            Buffers = [new glTFLoader.Schema.Buffer { ByteLength = buffer.Count }],
        };
        return (model, new AccessorReader(model, [buffer.ToArray()]));
    }

    internal static Accessor Vec4(Accessor.ComponentTypeEnum type, int count, bool normalized = false) =>
        new()
        {
            ComponentType = type,
            Type = Accessor.TypeEnum.VEC4,
            Count = count,
            Normalized = normalized,
        };

    internal static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static byte[] UShorts(params ushort[] values)
    {
        var bytes = new byte[values.Length * sizeof(ushort)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [TestMethod]
    public void ReadJointInfluences_ByteJoints_FloatWeights()
    {
        var reader = CreateReader(
            ([0, 1, 2, 3, 4, 5, 6, 7], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 2)),
            (Floats(0.5f, 0.25f, 0.25f, 0f, 1f, 0f, 0f, 0f), Vec4(Accessor.ComponentTypeEnum.FLOAT, 2))
        );
        var output = new FastList<GpuJointInfluence>();

        Assert.IsTrue(reader.ReadJointInfluences(0, 1, output, out var error), error);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(0u, output[0].Joint0);
        Assert.AreEqual(3u, output[0].Joint3);
        Assert.AreEqual(new Vector4(0.5f, 0.25f, 0.25f, 0f), output[0].Weights);
        Assert.AreEqual(4u, output[1].Joint0);
        Assert.AreEqual(7u, output[1].Joint3);
        Assert.AreEqual(Vector4.UnitX, output[1].Weights);
    }

    [TestMethod]
    public void ReadJointInfluences_ShortJoints_NormalizedWeights_AreRenormalized()
    {
        var reader = CreateReader(
            (UShorts(300, 1000, 0, 0), Vec4(Accessor.ComponentTypeEnum.UNSIGNED_SHORT, 1)),
            ([128, 128, 0, 0], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 1, normalized: true))
        );
        var output = new FastList<GpuJointInfluence>();

        Assert.IsTrue(reader.ReadJointInfluences(0, 1, output, out var error), error);

        Assert.AreEqual(300u, output[0].Joint0);
        Assert.AreEqual(1000u, output[0].Joint1);
        var w = output[0].Weights;
        Assert.AreEqual(1f, w.X + w.Y + w.Z + w.W, 1e-6f);
        Assert.AreEqual(0.5f, w.X, 1e-6f);
    }

    [TestMethod]
    public void ReadJointInfluences_ZeroWeights_BindToFirstJoint()
    {
        var reader = CreateReader(
            ([5, 0, 0, 0], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 1)),
            (Floats(0, 0, 0, 0), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1))
        );
        var output = new FastList<GpuJointInfluence>();

        Assert.IsTrue(reader.ReadJointInfluences(0, 1, output, out _));

        Assert.AreEqual(5u, output[0].Joint0);
        Assert.AreEqual(Vector4.UnitX, output[0].Weights);
    }

    [TestMethod]
    public void ReadJointInfluences_CountMismatch_Fails()
    {
        var reader = CreateReader(
            ([0, 0, 0, 0, 0, 0, 0, 0], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 2)),
            (Floats(1, 0, 0, 0), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1))
        );
        var output = new FastList<GpuJointInfluence>();

        Assert.IsFalse(reader.ReadJointInfluences(0, 1, output, out var error));
        Assert.IsNotNull(error);
        Assert.AreEqual(0, output.Count);
    }

    [TestMethod]
    public void ReadJointInfluences_FloatJoints_Fails()
    {
        var reader = CreateReader(
            (Floats(0, 1, 2, 3), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1)),
            (Floats(1, 0, 0, 0), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1))
        );

        Assert.IsFalse(reader.ReadJointInfluences(0, 1, [], out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ReadMatrices_ColumnMajorGltf_MapsToRowVectorMatrix()
    {
        // glTF stores a translation of (1, 2, 3) in elements 12..14 (column-major).
        var translation = Matrix4x4.CreateTranslation(1, 2, 3);
        var reader = CreateReader(
            (
                Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1),
                new Accessor
                {
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.MAT4,
                    Count = 1,
                }
            )
        );
        var output = new FastList<Matrix4x4>();

        Assert.IsTrue(reader.ReadMatrices(0, output));

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual(translation, output[0]);
    }

    [TestMethod]
    public void ReadMatrices_WrongType_Fails()
    {
        var reader = CreateReader((Floats(1, 0, 0, 0), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1)));

        Assert.IsFalse(reader.ReadMatrices(0, []));
    }
}
