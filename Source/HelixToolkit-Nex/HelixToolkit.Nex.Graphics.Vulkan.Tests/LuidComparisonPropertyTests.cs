using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.Tests.Vulkan;

/// <summary>
/// Feature: wpf-winui-integration, Property 3: LUID comparison is byte-exact
/// Validates: Requirements 2.2
/// </summary>
[TestClass]
public class LuidComparisonPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Property 3: For any two 8-byte arrays, LuidMatches returns true iff all bytes match.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [TestMethod]
    public void LuidComparison_ReturnsTrueIffAllBytesMatch()
    {
        var arb8 = ArbMap.Default.ArbFor<byte[]>().Filter(a => a is { Length: >= 8 });

        Prop.ForAll(
                arb8,
                arb8,
                (byte[] a, byte[] b) =>
                {
                    bool expected = a.AsSpan(0, 8).SequenceEqual(b.AsSpan(0, 8));
                    bool actual = VulkanContext.LuidMatches(a, b);
                    return expected == actual;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3 (identity): Identical 8-byte arrays always match.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [TestMethod]
    public void LuidComparison_IdenticalArraysAlwaysMatch()
    {
        var arb8 = ArbMap.Default.ArbFor<byte[]>().Filter(a => a is { Length: >= 8 });

        Prop.ForAll(
                arb8,
                (byte[] a) =>
                {
                    var copy = (byte[])a.Clone();
                    return VulkanContext.LuidMatches(a, copy);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3 (single-byte diff): Arrays differing in any single byte never match.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [TestMethod]
    public void LuidComparison_SingleByteDifference_ReturnsFalse()
    {
        var gen =
            from arr in ArbMap.Default.GeneratorFor<byte>().ArrayOf(8)
            from idx in Gen.Choose(0, 7)
            from delta in Gen.Choose(1, 255)
            select (arr, idx, (byte)delta);

        Prop.ForAll(
                Arb.From(gen),
                ((byte[] arr, int idx, byte delta) t) =>
                {
                    var modified = (byte[])t.arr.Clone();
                    modified[t.idx] = (byte)(modified[t.idx] ^ t.delta);
                    return !VulkanContext.LuidMatches(t.arr, modified);
                }
            )
            .Check(FsCheckConfig);
    }
}
