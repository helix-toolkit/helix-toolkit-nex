using FsCheck;
using FsCheck.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class MipMapCountTests
{
    // Feature: texture-loading, Property 13: For any non-negative integer n, (int)(MipMapCount)n == n
    [TestMethod]
    public void Property13_MipMapCount_IntRoundTrip()
    {
        Prop.ForAll(Arb.From(Gen.Choose(0, 10000)), (int n) => (int)(MipMapCount)n == n)
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void BoolTrue_ProducesCount0()
    {
        MipMapCount m = new MipMapCount(true);
        Assert.AreEqual(0, m.Count);
    }

    [TestMethod]
    public void BoolFalse_ProducesCount1()
    {
        MipMapCount m = new MipMapCount(false);
        Assert.AreEqual(1, m.Count);
    }

    [TestMethod]
    public void Auto_IsCount0()
    {
        Assert.AreEqual(0, MipMapCount.Auto.Count);
    }

    [TestMethod]
    public void ImplicitBoolTrue_IsAuto()
    {
        MipMapCount m = true;
        Assert.AreEqual(0, m.Count);
        Assert.IsTrue((bool)m);
    }

    [TestMethod]
    public void ImplicitBoolFalse_IsSingle()
    {
        MipMapCount m = false;
        Assert.AreEqual(1, m.Count);
        Assert.IsFalse((bool)m);
    }

    [TestMethod]
    public void NegativeCount_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => new MipMapCount(-1));
    }

    [TestMethod]
    public void Equality_SameCount_AreEqual()
    {
        Assert.AreEqual(new MipMapCount(5), new MipMapCount(5));
        Assert.IsTrue(new MipMapCount(5) == new MipMapCount(5));
        Assert.IsFalse(new MipMapCount(5) != new MipMapCount(5));
    }

    [TestMethod]
    public void Equality_DifferentCount_AreNotEqual()
    {
        Assert.AreNotEqual(new MipMapCount(3), new MipMapCount(7));
        Assert.IsTrue(new MipMapCount(3) != new MipMapCount(7));
    }
}
