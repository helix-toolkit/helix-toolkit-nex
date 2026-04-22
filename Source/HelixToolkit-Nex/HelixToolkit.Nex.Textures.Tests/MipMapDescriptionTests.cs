using FsCheck;
using FsCheck.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class MipMapDescriptionTests
{
    // Feature: texture-loading, Property 14: For any MipMapDescription, MipmapSize == DepthStride * Depth
    [TestMethod]
    public void Property14_MipMapDescription_MipmapSizeInvariant()
    {
        var gen =
            from w in Gen.Choose(1, 512)
            from h in Gen.Choose(1, 512)
            from d in Gen.Choose(1, 64)
            from rs in Gen.Choose(1, 4096)
            from ds in Gen.Choose(1, 4096)
            select new MipMapDescription(w, h, d, rs, ds, w, h);
        Prop.ForAll(Arb.From(gen), desc => desc.MipmapSize == desc.DepthStride * desc.Depth)
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Constructor_SetsAllFields()
    {
        var desc = new MipMapDescription(64, 32, 4, 256, 8192, 64, 32);
        Assert.AreEqual(64, desc.Width);
        Assert.AreEqual(32, desc.Height);
        Assert.AreEqual(4, desc.Depth);
        Assert.AreEqual(256, desc.RowStride);
        Assert.AreEqual(8192, desc.DepthStride);
        Assert.AreEqual(8192 * 4, desc.MipmapSize);
        Assert.AreEqual(64, desc.WidthPacked);
        Assert.AreEqual(32, desc.HeightPacked);
    }

    [TestMethod]
    public void Equality_SameValues_AreEqual()
    {
        var a = new MipMapDescription(4, 4, 1, 16, 64, 4, 4);
        var b = new MipMapDescription(4, 4, 1, 16, 64, 4, 4);
        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
    }

    [TestMethod]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new MipMapDescription(4, 4, 1, 16, 64, 4, 4);
        var b = new MipMapDescription(8, 4, 1, 32, 128, 8, 4);
        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }
}
