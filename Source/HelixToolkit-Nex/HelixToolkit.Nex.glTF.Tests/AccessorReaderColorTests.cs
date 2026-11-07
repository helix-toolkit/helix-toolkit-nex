using System.Numerics;
using glTFLoader.Schema;
using static HelixToolkit.Nex.glTF.Tests.AccessorReaderSkinTests;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Tests for reading <c>COLOR_0</c> data in every component type glTF allows.
/// </summary>
[TestClass]
public class AccessorReaderColorTests
{
    [TestMethod]
    public void ReadColors_NormalizedUnsignedByteVec4()
    {
        var reader = CreateReader(
            ([255, 0, 51, 255, 0, 255, 0, 102], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 2, normalized: true))
        );
        var output = new FastList<Vector4>();

        reader.ReadColors(0, output);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(new Vector4(1f, 0f, 0.2f, 1f), output[0]);
        Assert.AreEqual(new Vector4(0f, 1f, 0f, 0.4f), output[1]);
    }

    [TestMethod]
    public void ReadColors_NormalizedUnsignedShortVec3_DefaultsAlphaToOne()
    {
        var accessor = new Accessor
        {
            ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
            Type = Accessor.TypeEnum.VEC3,
            Count = 1,
            Normalized = true,
        };
        var reader = CreateReader((UShorts(65535, 0, 13107), accessor));
        var output = new FastList<Vector4>();

        reader.ReadColors(0, output);

        Assert.AreEqual(new Vector4(1f, 0f, 0.2f, 1f), output[0]);
    }

    [TestMethod]
    public void ReadColors_FloatVec4()
    {
        var reader = CreateReader(
            (Floats(0.25f, 0.5f, 0.75f, 1f), Vec4(Accessor.ComponentTypeEnum.FLOAT, 1))
        );
        var output = new FastList<Vector4>();

        reader.ReadColors(0, output);

        Assert.AreEqual(new Vector4(0.25f, 0.5f, 0.75f, 1f), output[0]);
    }
}
