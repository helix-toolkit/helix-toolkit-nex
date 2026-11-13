using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using static HelixToolkit.Nex.glTF.Tests.AccessorReaderSkinTests;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Tests for reading <c>TEXCOORD_n</c> data in every component type glTF allows, for both UV sets.
/// </summary>
[TestClass]
public class AccessorReaderTexCoordTests
{
    private static Accessor Vec2(Accessor.ComponentTypeEnum type, int count, bool normalized) =>
        new()
        {
            ComponentType = type,
            Type = Accessor.TypeEnum.VEC2,
            Count = count,
            Normalized = normalized,
        };

    [TestMethod]
    public void ReadTexCoords_Float()
    {
        var reader = CreateReader(
            (Floats(0.25f, 0.5f, 0.75f, 1f), Vec2(Accessor.ComponentTypeEnum.FLOAT, 2, false))
        );
        var output = new FastList<VertexProperties>();

        reader.ReadTexCoords(0, output, merge: false);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(new Vector2(0.25f, 0.5f), output[0].TexCoord);
        Assert.AreEqual(new Vector2(0.75f, 1f), output[1].TexCoord);
    }

    [TestMethod]
    public void ReadTexCoords_NormalizedUnsignedByte()
    {
        var reader = CreateReader(
            ([255, 0, 51, 255], Vec2(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 2, true))
        );
        var output = new FastList<VertexProperties>();

        reader.ReadTexCoords(0, output, merge: false);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(new Vector2(1f, 0f), output[0].TexCoord);
        Assert.AreEqual(new Vector2(0.2f, 1f), output[1].TexCoord);
    }

    [TestMethod]
    public void ReadTexCoords_NormalizedUnsignedShort_SecondSet()
    {
        var reader = CreateReader(
            (UShorts(65535, 0, 13107, 65535), Vec2(Accessor.ComponentTypeEnum.UNSIGNED_SHORT, 2, true))
        );
        var output = new FastList<VertexProperties>();

        reader.ReadTexCoords(0, output, merge: false, secondSet: true);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(new Vector2(1f, 0f), output[0].TexCoord1);
        Assert.AreEqual(new Vector2(0.2f, 1f), output[1].TexCoord1);
        Assert.AreEqual(Vector2.Zero, output[0].TexCoord, "The first set is untouched.");
    }

    [TestMethod]
    public void ReadTexCoords_SecondSetMerge_KeepsFirstSet()
    {
        var reader = CreateReader(
            (Floats(0.1f, 0.2f), Vec2(Accessor.ComponentTypeEnum.FLOAT, 1, false)),
            (Floats(0.9f, 0.8f), Vec2(Accessor.ComponentTypeEnum.FLOAT, 1, false))
        );
        var output = new FastList<VertexProperties>();

        reader.ReadTexCoords(0, output, merge: false);
        reader.ReadTexCoords(1, output, merge: true, secondSet: true);

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual(new Vector2(0.1f, 0.2f), output[0].TexCoord);
        Assert.AreEqual(new Vector2(0.9f, 0.8f), output[0].TexCoord1);
    }
}
